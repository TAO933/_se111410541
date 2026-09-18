using Microsoft.Win32;

namespace Vault.App.Services;

/// <summary>
/// Reads clipboard history / cross-device sync posture from the registry.
/// Missing values = Windows default (unknown) → we warn conservatively.
/// Refs: HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory =1 means
/// Win+V history keeps our password; HKLM\SOFTWARE\Policies\Microsoft\Windows\System
/// \AllowCrossDeviceClipboard =0 means sync is policy-disabled (safe).
/// </summary>
public static class ClipboardPolicy
{
    public static bool IsHistoryEnabled()
    {
        try
        {
            object? v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Clipboard",
                "EnableClipboardHistory", null);
            return v is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when sync is NOT hard-disabled by policy. We cannot distinguish
    /// "user turned sync on" from default, so null/1 → treat as possibly syncing.
    /// </summary>
    public static bool IsSyncPossiblyOn()
    {
        try
        {
            object? machine = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System",
                "AllowCrossDeviceClipboard", null);
            if (machine is int m && m == 0)
                return false;
            object? user = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\System",
                "AllowCrossDeviceClipboard", null);
            if (user is int u && u == 0)
                return false;
            return true;
        }
        catch
        {
            return true;
        }
    }

    public static string? GetWarning()
    {
        bool history = IsHistoryEnabled();
        bool sync = IsSyncPossiblyOn();
        if (history && sync)
            return "⚠ 偵測到剪貼簿歷史開啟且跨裝置同步可能開啟：密碼會留在 Win+V 歷史並可能上雲。建議到 設定→系統→剪貼簿 關閉歷史/同步，或複製後按 Win+V 刪除該筆。";
        if (history)
            return "⚠ 剪貼簿歷史開啟中：密碼會留在 Win+V 歷史。複製後建議按 Win+V 刪除該筆，或到 設定→系統→剪貼簿 關閉。";
        if (sync)
            return "⚠ 跨裝置剪貼簿同步可能開啟：密碼可能上傳到微軟雲。建議到 設定→系統→剪貼簿 關閉同步，或選「手動同步」。";
        return null;
    }
}
