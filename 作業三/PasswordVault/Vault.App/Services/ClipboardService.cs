using Windows.ApplicationModel.DataTransfer;

namespace Vault.App.Services;

/// <summary>
/// Clipboard downgrade for anti-cheat / UAC windows where SendInput is blocked.
/// Copies plaintext for at most <see cref="DefaultTtl"/>, then clears ONLY if
/// the clipboard still holds our text (never wipes the user's newer copy).
/// Must be called on the UI thread (WinUI Clipboard requirement).
/// </summary>
public static class ClipboardService
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(10);

    public static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text ?? "");
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public static void Clear() => Clipboard.Clear();

    /// <summary>
    /// Clear only when the current clipboard text still equals <paramref name="token"/>.
    /// Returns true when we actually cleared.
    /// </summary>
    public static async Task<bool> ClearIfMatchesAsync(string token)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
                return false;
            string current = await content.GetTextAsync();
            if (!string.Equals(current, token, StringComparison.Ordinal))
                return false; // user copied something else; leave it alone
            Clipboard.Clear();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
