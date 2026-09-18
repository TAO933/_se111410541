using System.Runtime.InteropServices;

namespace Vault.App.Services;

/// <summary>
/// Global hotkeys via RegisterHotKey on the main window.
/// Ctrl+Shift+L = Auto-Type, Ctrl+Shift+C = copy matched password to clipboard.
/// Delivered on the UI thread through window subclassing.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int HotkeyIdType = 0xB001;
    public const int HotkeyIdCopy = 0xB002;

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_L = 0x4C;
    private const uint VK_C = 0x43;
    private const uint WM_HOTKEY = 0x0312;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(
        nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public event EventHandler? HotkeyPressed;
    public event EventHandler? CopyHotkeyPressed;

    private nint _hwnd;
    private SubclassProc? _proc;
    private bool _disposed;

    public void Start(nint hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hwnd != 0)
            throw new InvalidOperationException("Hotkey already started.");
        _proc = WndProc;
        if (!SetWindowSubclass(hwnd, _proc, 0, 0))
            throw new System.ComponentModel.Win32Exception("SetWindowSubclass failed.");
        bool okL = RegisterHotKey(hwnd, HotkeyIdType, MOD_CONTROL | MOD_SHIFT, VK_L);
        bool okC = RegisterHotKey(hwnd, HotkeyIdCopy, MOD_CONTROL | MOD_SHIFT, VK_C);
        if (!okL || !okC)
        {
            if (okL) UnregisterHotKey(hwnd, HotkeyIdType);
            if (okC) UnregisterHotKey(hwnd, HotkeyIdCopy);
            RemoveWindowSubclass(hwnd, _proc, 0);
            _proc = null;
            throw new System.ComponentModel.Win32Exception("RegisterHotKey failed. Hotkey may be taken by another app.");
        }
        _hwnd = hwnd;
    }

    private nint WndProc(
        nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WM_HOTKEY)
        {
            if (wParam == (nint)HotkeyIdType)
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            else if (wParam == (nint)HotkeyIdCopy)
                CopyHotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hwnd != 0)
        {
            UnregisterHotKey(_hwnd, HotkeyIdType);
            UnregisterHotKey(_hwnd, HotkeyIdCopy);
            if (_proc != null)
                RemoveWindowSubclass(_hwnd, _proc, 0);
            _hwnd = 0;
            _proc = null;
        }
    }
}
