using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Vault.App.Services;

/// <summary>
/// Same-user named pipe server for Vault.NativeHost (the browser extension bridge).
/// Vault.App must be running and unlocked; otherwise ops return {ok:false}.
/// </summary>
public sealed class PipeServer : IDisposable
{
    public const string PipeName = "passwordvault-vault";

    private readonly VaultService _vault;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public PipeServer(VaultService vault)
    {
        _vault = vault;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loop != null)
            return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => ListenAsync(_cts.Token));
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server = CreateServer();
            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(server, ct), ct);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch
            {
                server.Dispose();
            }
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        var ps = new PipeSecurity();
        var sid = WindowsIdentity.GetCurrent().User;
        if (sid != null)
            ps.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, ps);
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: false)
                {
                    AutoFlush = true
                };
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                await writer.WriteLineAsync(Dispatch(line)).ConfigureAwait(false);
            }
            catch
            {
                // Client went away; nothing to do.
            }
        }
    }

    private string Dispatch(string? line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line ?? "");
            var root = doc.RootElement;
            string op = root.TryGetProperty("op", out var o) ? o.GetString() ?? "" : "";
            return op switch
            {
                "ping" => JsonSerializer.Serialize(new { ok = true, version = 1 }),
                "get-login" => GetLogin(root),
                "save-login" => SaveLogin(root),
                _ => Err("unknown op"),
            };
        }
        catch (Exception ex)
        {
            return Err(ex.Message);
        }
    }

    private string GetLogin(JsonElement root)
    {
        if (!_vault.IsUnlocked)
            return JsonSerializer.Serialize(new { ok = false, error = "vault locked" });
        string host = Str(root, "host");
        var items = _vault.GetLoginsForHost(host)
            .Select(c => new { id = c.Id, name = c.Name, username = c.Username, password = c.Password });
        return JsonSerializer.Serialize(new { ok = true, items });
    }

    private string SaveLogin(JsonElement root)
    {
        if (!_vault.IsUnlocked)
            return JsonSerializer.Serialize(new { ok = false, error = "vault locked" });
        string host = Str(root, "host");
        string name = Str(root, "name");
        string user = Str(root, "username");
        string pass = Str(root, "password");
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            return Err("username and password required");
        _vault.Add(string.IsNullOrEmpty(name) ? host : name, host, user, pass);
        return JsonSerializer.Serialize(new { ok = true });
    }

    private static string Str(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string Err(string message) =>
        JsonSerializer.Serialize(new { ok = false, error = message });

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Shutting down; ignore.
        }
        _cts?.Dispose();
    }
}
