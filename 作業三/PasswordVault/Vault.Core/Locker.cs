using System.Security.Cryptography;

namespace Vault.Core;

/// <summary>
/// Holds the unlocked vault key in memory. Lock() zeroes it.
/// Exactly one Unlock at a time. Auto-locks after timeout.
/// </summary>
public sealed class Locker : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _key;
    private Timer? _timer;
    private bool _disposed;

    public TimeSpan AutoLockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public event EventHandler? Locked;

    public bool IsUnlocked
    {
        get { lock (_gate) { return _key != null && !_disposed; } }
    }

    public void Unlock(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != Kdf.KeySize)
            throw new ArgumentException($"Key must be {Kdf.KeySize} bytes.", nameof(key));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            LockInternal();
            _key = (byte[])key.Clone();
            _timer = new Timer(_ => Lock(), null, AutoLockTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Reset the auto-lock countdown (call on user activity).</summary>
    public void Touch()
    {
        lock (_gate)
        {
            if (_key == null || _disposed) return;
            _timer?.Change(AutoLockTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    public void Lock()
    {
        EventHandler? h;
        lock (_gate)
        {
            h = LockInternal();
        }
        h?.Invoke(this, EventArgs.Empty);
    }

    private EventHandler? LockInternal()
    {
        if (_key != null)
        {
            CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }
        _timer?.Dispose();
        _timer = null;
        var h = Locked;
        Locked = null;
        return h;
    }

    /// <summary>Internal key reference. Caller must NOT hold or mutate it. Use GetKeyCopy for safe use.</summary>
    public byte[] Key
    {
        get
        {
            lock (_gate)
            {
                return _key ?? throw new InvalidOperationException("Vault is locked.");
            }
        }
    }

    public byte[] GetKeyCopy()
    {
        lock (_gate)
        {
            return _key == null
                ? throw new InvalidOperationException("Vault is locked.")
                : (byte[])_key.Clone();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            LockInternal();
        }
        GC.SuppressFinalize(this);
    }
}
