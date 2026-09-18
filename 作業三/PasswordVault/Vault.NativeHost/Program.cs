using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Vault.NativeHost;

namespace Vault.NativeHost;

// Chrome starts us with stdio redirected. Speak the Native Messaging
// framing (u32LE length + UTF-8 JSON) and forward vault ops to Vault.App
// over a same-user named pipe. We never see the master password or key.
internal static class Program
{
    internal static async Task<int> Main() => await Host.RunAsync();
}

internal static class Host
{
    internal const string PipeName = "passwordvault-vault";
    private const int PipeTimeoutMs = 5000;
    private const int MaxMessageBytes = 1_000_000;

    internal static async Task<int> RunAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        while (true)
        {
            byte[]? lenBuf = await ReadExactlyAsync(stdin, 4);
            if (lenBuf is null)
                return 0; // EOF: browser closed us
            int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            if (len <= 0 || len > MaxMessageBytes)
                return 1;
            byte[]? payload = await ReadExactlyAsync(stdin, len);
            if (payload is null)
                return 1;

            string response = await DispatchAsync(Encoding.UTF8.GetString(payload));
            byte[] outBytes = Encoding.UTF8.GetBytes(response);
            byte[] outLen = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(outLen, outBytes.Length);
            await stdout.WriteAsync(outLen);
            await stdout.WriteAsync(outBytes);
            await stdout.FlushAsync();
        }
    }

    private static async Task<byte[]?> ReadExactlyAsync(Stream stream, int count)
    {
        byte[] buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buf.AsMemory(read, count - read));
            if (n == 0)
                return null;
            read += n;
        }
        return buf;
    }

    private static async Task<string> DispatchAsync(string request)
    {
        try
        {
            using var doc = JsonDocument.Parse(request);
            string op = doc.RootElement.TryGetProperty("op", out var o)
                ? o.GetString() ?? ""
                : "";
            return op switch
            {
                "ping" => JsonSerializer.Serialize(new { ok = true, version = 1 }),
                "get-login" or "save-login" => await ForwardToVaultAsync(request),
                _ => JsonSerializer.Serialize(new { ok = false, error = "unknown op" }),
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }
    }

    private static async Task<string> ForwardToVaultAsync(string request)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(PipeTimeoutMs);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: false);
            await writer.WriteLineAsync(request);
            string? line = await reader.ReadLineAsync()
                .WaitAsync(TimeSpan.FromMilliseconds(PipeTimeoutMs));
            return string.IsNullOrEmpty(line)
                ? JsonSerializer.Serialize(new { ok = false, error = "empty vault response" })
                : line;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = $"vault unreachable: {ex.Message}" });
        }
    }
}
