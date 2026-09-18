using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Vault.App.Services;

internal static class ForegroundWindow
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    public static (string Title, string ProcessName) GetActive()
    {
        nint h = GetForegroundWindow();
        var sb = new StringBuilder(512);
        string title = GetWindowText(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        string process = "";
        try
        {
            GetWindowThreadProcessId(h, out uint pid);
            process = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            // Process exited or access denied; title-only matching still works.
        }
        return (title, process);
    }
}
