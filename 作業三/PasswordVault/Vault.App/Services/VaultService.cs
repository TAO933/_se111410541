using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Vault.Core;

namespace Vault.App.Services;

public sealed record VaultItem(string Id, string Name);

public sealed record LoginCredential(string Id, string Name, string Username, string Password);

public sealed record AutoTypeMatch(string Id, string Name, string Username, string Password, string Sequence);

/// <summary>
/// UI-facing vault session. Owns Locker (in-memory key) + VaultStore (SQLCipher file).
/// Salt lives in a sidecar file (non-secret); verifier is SHA256(key), never the key itself.
/// </summary>
public sealed class VaultService : IDisposable
{
    private readonly string _dbPath;
    private readonly string _saltPath;
    private readonly Locker _locker = new();
    private VaultStore? _store;
    private bool _disposed;

    public VaultService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PasswordVault");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "vault.db");
        _saltPath = Path.Combine(dir, "vault.salt");
    }

    public bool IsUnlocked => _locker.IsUnlocked;
    public bool VaultExists() => File.Exists(_dbPath) && File.Exists(_saltPath);

    public async Task CreateAsync(string master)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (VaultExists())
            throw new InvalidOperationException("密碼庫已存在，請直接解鎖。");
        ValidateMaster(master);

        byte[] salt = Kdf.GenerateSalt();
        byte[] key = await Task.Run(() => Kdf.DeriveKey(master, salt)).ConfigureAwait(false);
        try
        {
            byte[] verifier = SHA256.HashData(key);
            try
            {
                File.WriteAllBytes(_saltPath, salt);
                CloseStore();
                var store = new VaultStore(_dbPath);
                store.Open(key);
                store.InitNewVault(salt, verifier);
                _store = store;
                _locker.Unlock(key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(verifier);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task UnlockAsync(string master)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!VaultExists())
            throw new InvalidOperationException("尚未建立密碼庫，請先建立。");
        if (string.IsNullOrEmpty(master))
            throw new ArgumentException("請輸入主密碼。", nameof(master));

        byte[] salt = File.ReadAllBytes(_saltPath);
        if (salt.Length != Kdf.SaltSize)
            throw new InvalidDataException("Salt 檔損毀。");

        byte[] key = await Task.Run(() => Kdf.DeriveKey(master, salt)).ConfigureAwait(false);
        try
        {
            var store = new VaultStore(_dbPath);
            bool ok = false;
            try
            {
                store.Open(key);
                if (!store.TryLoadMeta(out _, out byte[] verifier))
                    throw new InvalidOperationException("密碼庫損毀：讀不到驗證資料。");
                try
                {
                    byte[] expect = SHA256.HashData(key);
                    try
                    {
                        if (!Kdf.Verify(expect, verifier))
                            throw new UnauthorizedAccessException("主密碼錯誤。");
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(expect);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(verifier);
                }
                CloseStore();
                _store = store;
                _locker.Unlock(key);
                ok = true;
            }
            catch (SqliteException ex)
            {
                throw new UnauthorizedAccessException("主密碼錯誤或資料庫損毀。", ex);
            }
            finally
            {
                if (!ok)
                    store.Dispose();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void Lock()
    {
        CloseStore();
        _locker.Lock();
    }

    public IReadOnlyList<VaultItem> LoadItems()
    {
        var store = _store ?? throw new InvalidOperationException("尚未解鎖。");
        byte[] key = _locker.GetKeyCopy();
        try
        {
            return store.ListItems()
                .Select(r => new VaultItem(r.Id, Crypto.DecryptString(key, r.NameEnc)))
                .ToList();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void Add(string name, string url, string username, string password)
    {
        var store = _store ?? throw new InvalidOperationException("尚未解鎖。");
        byte[] key = _locker.GetKeyCopy();
        try
        {
            store.UpsertItem(
                Guid.NewGuid().ToString("N"),
                Crypto.EncryptString(key, name),
                VaultStore.ComputeUrlHash(url ?? ""),
                Crypto.EncryptString(key, url ?? ""),
                Crypto.EncryptString(key, username),
                Crypto.EncryptString(key, password));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Exact-host match for autofill. Decrypts name+url to filter, then
    /// decrypts credentials only for matches.
    /// </summary>
    public IReadOnlyList<LoginCredential> GetLoginsForHost(string host)    {
        var store = _store ?? throw new InvalidOperationException("尚未解鎖。");
        if (string.IsNullOrWhiteSpace(host))
            return [];
        string want = host.Trim().ToLowerInvariant();
        byte[] key = _locker.GetKeyCopy();
        try
        {
            var result = new List<LoginCredential>();
            foreach (var r in store.ListItems())
            {
                string url = Crypto.DecryptString(key, r.UrlEnc);
                if (!IsHostMatch(url, want))
                    continue;
                result.Add(new LoginCredential(
                    r.Id,
                    Crypto.DecryptString(key, r.NameEnc),
                    Crypto.DecryptString(key, r.UsernameEnc),
                    Crypto.DecryptString(key, r.PasswordEnc)));
            }
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool IsHostMatch(string url, string wantHost)
    {
        try
        {
            string candidate = url.Contains("://") ? url : "https://" + url;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                return false;
            return string.Equals(uri.Host.ToLowerInvariant(), wantHost, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Foreground window title/process regex match for Auto-Type.
    /// Checks title first, then process name. Invalid regex never matches.
    /// </summary>
    public AutoTypeMatch? FindAutoTypeMatch(string title, string processName)
    {
        var store = _store ?? throw new InvalidOperationException("尚未解鎖。");
        byte[] key = _locker.GetKeyCopy();
        try
        {
            foreach (var r in store.ListItems())
            {
                if (string.IsNullOrWhiteSpace(r.WindowRegex))
                    continue;
                if (!AutoTypeSequence.TitleMatches(title ?? "", r.WindowRegex) &&
                    !AutoTypeSequence.TitleMatches(processName ?? "", r.WindowRegex))
                    continue;
                return new AutoTypeMatch(
                    r.Id,
                    Crypto.DecryptString(key, r.NameEnc),
                    Crypto.DecryptString(key, r.UsernameEnc),
                    Crypto.DecryptString(key, r.PasswordEnc),
                    string.IsNullOrWhiteSpace(r.AutotypeSeq) ? AutoTypeSequence.Default : r.AutotypeSeq);
            }
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string GetWindowRegex(string id)
    {
        var store = _store ?? throw new InvalidOperationException("尚未解鎖。");
        var row = store.ListItems().FirstOrDefault(r => r.Id == id)
            ?? throw new InvalidOperationException("找不到該項目。");
        return row.WindowRegex ?? "";
    }

    /// <summary>Bind a window-title regex to an item. Empty clears the binding.</summary>
    public void BindWindow(string id, string? regex)
    {
        var store = _store ?? throw new InvalidOperationException("尚未解鎖。");
        string? pattern = string.IsNullOrWhiteSpace(regex) ? null : regex.Trim();
        if (pattern != null)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(200));
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Regex 無效：{ex.Message}", nameof(regex));
            }
        }
        store.UpdateWindowRegex(id, pattern);
    }

    /// <summary>Decrypt a single item's credentials (for clipboard copy). Caller trims lifetime.</summary>
    public LoginCredential GetCredential(string id)
    {
        var store = _store ?? throw new InvalidOperationException("尚未解鎖。");
        var row = store.ListItems().FirstOrDefault(r => r.Id == id)
            ?? throw new InvalidOperationException("找不到該項目。");
        byte[] key = _locker.GetKeyCopy();
        try
        {
            return new LoginCredential(
                row.Id,
                Crypto.DecryptString(key, row.NameEnc),
                Crypto.DecryptString(key, row.UsernameEnc),
                Crypto.DecryptString(key, row.PasswordEnc));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void ValidateMaster(string master)
    {
        if (string.IsNullOrEmpty(master) || master.Length < 12)
            throw new ArgumentException("主密碼至少需要 12 個字元。", nameof(master));
    }

    private void CloseStore()
    {
        _store?.Dispose();
        _store = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CloseStore();
        _locker.Dispose();
    }
}
