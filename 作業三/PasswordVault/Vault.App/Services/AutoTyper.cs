using System.Diagnostics;
using System.Runtime.InteropServices;
using Vault.Core;

namespace Vault.App.Services;

/// <summary>
/// Keystroke injection via SendInput (Unicode mode, so symbols/CJK survive).
/// Runs off the UI thread; caller must ensure the target window is foreground.
/// </summary>
public static class AutoTyper
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf<INPUT>();
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public static void TypeSequence(string? sequence, string username, string password)
    {
        foreach (var token in AutoTypeSequence.Parse(sequence))
        {
            switch (token.Kind)
            {
                case AutoTypeTokenKind.Username:
                    TypeText(username);
                    break;
                case AutoTypeTokenKind.Password:
                    TypeText(password);
                    break;
                case AutoTypeTokenKind.Tab:
                    PressVirtualKey(VK_TAB);
                    break;
                case AutoTypeTokenKind.Enter:
                    PressVirtualKey(VK_RETURN);
                    break;
                default:
                    TypeText(token.Text);
                    break;
            }
            Thread.Sleep(15);
        }
    }

    private static void TypeText(string text)
    {
        foreach (char c in text)
        {
            INPUT[] inputs =
            [
                new() { type = INPUT_KEYBOARD, U = new() { ki = new() { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE } } },
                new() { type = INPUT_KEYBOARD, U = new() { ki = new() { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } },
            ];
            SendInput(2, inputs, INPUT.Size);
            Thread.Sleep(10);
        }
    }

    private static void PressVirtualKey(ushort vk)
    {
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        INPUT[] inputs =
        [
            new() { type = INPUT_KEYBOARD, U = new() { ki = new() { wVk = vk, wScan = scan, dwFlags = 0 } } },
            new() { type = INPUT_KEYBOARD, U = new() { ki = new() { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_KEYUP } } },
        ];
        SendInput(2, inputs, INPUT.Size);
        Thread.Sleep(30);
    }

    /// <summary>
    /// Wait until Ctrl/Shift/Alt are released so held hotkey modifiers
    /// don't leak into the typed output. Returns on timeout (best effort).
    /// </summary>
    public static async Task WaitForModifiersReleasedAsync(int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsDown(VK_CONTROL) && !IsDown(VK_SHIFT) && !IsDown(VK_MENU))
                return;
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
