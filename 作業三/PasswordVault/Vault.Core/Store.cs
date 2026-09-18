using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Vault.Core;

/// <summary>
/// SQLCipher-backed store. File-level encryption via PRAGMA key,
/// column-level AES-GCM blobs handled by caller via <see cref="Crypto"/>.
/// </summary>
public sealed class VaultStore : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;
    private bool _disposed;

    public VaultStore(string dbPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(dbPath);
        _dbPath = dbPath;
    }

    public void Open(byte[] rawKey)
    {
        ArgumentNullException.ThrowIfNull(rawKey);
        if (rawKey.Length != Kdf.KeySize)
            throw new ArgumentException($"Key must be {Kdf.KeySize} bytes.", nameof(rawKey));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_conn != null) throw new InvalidOperationException("Already open.");

        SQLitePCL.Batteries_V2.Init();

        var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(rawKey)}\";";
            cmd.ExecuteNonQuery();
        }
        // Verify key: query without SELECT on cipher will throw on wrong key at first read.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS vault_meta(
                  id INTEGER PRIMARY KEY CHECK(id=1),
                  salt BLOB NOT NULL,
                  verifier BLOB NOT NULL);
                CREATE TABLE IF NOT EXISTS items(
                  id TEXT PRIMARY KEY,
                  folder_id TEXT,
                  name_enc BLOB NOT NULL,
                  url_hash TEXT NOT NULL,
                  url_enc BLOB NOT NULL,
                  username_enc BLOB NOT NULL,
                  password_enc BLOB NOT NULL,
                  totp_enc BLOB,
                  notes_enc BLOB,
                  window_title_regex TEXT,
                  autotype_seq TEXT NOT NULL DEFAULT '{USERNAME}{TAB}{PASSWORD}{ENTER}',
                  revision INTEGER NOT NULL DEFAULT 1,
                  created_utc INTEGER NOT NULL,
                  updated_utc INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_items_url_hash ON items(url_hash);
                CREATE TABLE IF NOT EXISTS history(
                  item_id TEXT NOT NULL,
                  revision INTEGER NOT NULL,
                  snapshot_enc BLOB NOT NULL,
                  PRIMARY KEY(item_id, revision));
                """;
            cmd.ExecuteNonQuery();
        }

        _conn = conn;
    }

    private SqliteConnection Conn =>
        _conn ?? throw new InvalidOperationException("Call Open() first.");

    public void InitNewVault(byte[] salt, byte[] verifier)
    {
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(verifier);
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO vault_meta(id, salt, verifier) VALUES(1, $s, $v);";
        cmd.Parameters.AddWithValue("$s", salt);
        cmd.Parameters.AddWithValue("$v", verifier);
        cmd.ExecuteNonQuery();
    }

    public bool TryLoadMeta(out byte[] salt, out byte[] verifier)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT salt, verifier FROM vault_meta WHERE id=1;";
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            salt = []; verifier = [];
            return false;
        }
        salt = (byte[])r["salt"];
        verifier = (byte[])r["verifier"];
        return true;
    }

    public void UpsertItem(string id, byte[] nameEnc, string urlHash, byte[] urlEnc,
        byte[] usernameEnc, byte[] passwordEnc, byte[]? totpEnc = null,
        byte[]? notesEnc = null, string? windowRegex = null)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO items(id, name_enc, url_hash, url_enc, username_enc, password_enc,
              totp_enc, notes_enc, window_title_regex, created_utc, updated_utc)
            VALUES($id,$n,$h,$u,$user,$pass,$totp,$notes,$w,$now,$now)
            ON CONFLICT(id) DO UPDATE SET
              name_enc=excluded.name_enc, url_hash=excluded.url_hash, url_enc=excluded.url_enc,
              username_enc=excluded.username_enc, password_enc=excluded.password_enc,
              totp_enc=excluded.totp_enc, notes_enc=excluded.notes_enc,
              window_title_regex=excluded.window_title_regex,
              revision=items.revision+1, updated_utc=excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", nameEnc);
        cmd.Parameters.AddWithValue("$h", urlHash);
        cmd.Parameters.AddWithValue("$u", urlEnc);
        cmd.Parameters.AddWithValue("$user", usernameEnc);
        cmd.Parameters.AddWithValue("$pass", passwordEnc);
        cmd.Parameters.AddWithValue("$totp", (object?)totpEnc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)notesEnc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$w", (object?)windowRegex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    public int CountItems()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM items;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public sealed record ItemRow(string Id, string UrlHash, byte[] NameEnc, byte[] UrlEnc, byte[] UsernameEnc, byte[] PasswordEnc, string? WindowRegex, string AutotypeSeq);

    public IReadOnlyList<ItemRow> ListItems()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id, url_hash, name_enc, url_enc, username_enc, password_enc, window_title_regex, autotype_seq FROM items ORDER BY created_utc DESC, rowid DESC;";
        using var r = cmd.ExecuteReader();
        var list = new List<ItemRow>();
        while (r.Read())
            list.Add(new ItemRow(
                r.GetString(0), r.GetString(1),
                (byte[])r["name_enc"], (byte[])r["url_enc"],
                (byte[])r["username_enc"], (byte[])r["password_enc"],
                r["window_title_regex"] as string,
                r["autotype_seq"] as string ?? AutoTypeSequence.Default));
        return list;
    }

    public void UpdateWindowRegex(string id, string? regex)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET window_title_regex=$w, revision=revision+1, updated_utc=$now WHERE id=$id;";
        cmd.Parameters.AddWithValue("$w", (object?)regex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("找不到該項目。");
    }

    public static string ComputeUrlHash(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        string norm = url.Trim().ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn?.Dispose();
        _conn = null;
        SqliteConnection.ClearAllPools();
        GC.SuppressFinalize(this);
    }
}
