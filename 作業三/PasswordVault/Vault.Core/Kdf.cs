using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Vault.Core;

/// <summary>
/// Argon2id KDF. Master password never stored, only salt + verifier in DB.
/// </summary>
public static class Kdf
{
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int MemoryKb = 65536; // 64 MB
    public const int Iterations = 3;
    public const int Parallelism = 4;

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] DeriveKey(string masterPassword, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterPassword);
        ArgumentNullException.ThrowIfNull(salt);
        byte[] pwdBytes = Encoding.UTF8.GetBytes(masterPassword);
        try
        {
            return DeriveKey(pwdBytes, salt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwdBytes);
        }
    }

    public static byte[] DeriveKey(byte[] passwordBytes, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(passwordBytes);
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length < SaltSize)
            throw new ArgumentException($"Salt must be at least {SaltSize} bytes.", nameof(salt));

        using var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            DegreeOfParallelism = Parallelism,
            MemorySize = MemoryKb,
            Iterations = Iterations,
        };
        return argon2.GetBytes(KeySize);
    }

    public static bool Verify(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);
}
