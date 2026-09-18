using System.Security.Cryptography;
using System.Text;

namespace Vault.Core;

/// <summary>
/// AES-256-GCM authenticated encryption. Blob layout: nonce(12) || cipher || tag(16).
/// </summary>
public static class Crypto
{
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32;

    public static byte[] Encrypt(byte[] key, byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plain);
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes.", nameof(key));

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plain, cipher, tag);

        byte[] blob = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, NonceSize + cipher.Length, TagSize);
        CryptographicOperations.ZeroMemory(cipher);
        return blob;
    }

    public static byte[] Decrypt(byte[] key, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(blob);
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes.", nameof(key));
        if (blob.Length < NonceSize + TagSize)
            throw new ArgumentException("Blob too short.", nameof(blob));

        byte[] nonce = blob[..NonceSize];
        byte[] cipher = blob[NonceSize..^TagSize];
        byte[] tag = blob[^TagSize..];
        byte[] plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    public static byte[] EncryptString(byte[] key, string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        byte[] bytes = Encoding.UTF8.GetBytes(plain);
        try
        {
            return Encrypt(key, bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static string DecryptString(byte[] key, byte[] blob)
    {
        byte[] plain = Decrypt(key, blob);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
