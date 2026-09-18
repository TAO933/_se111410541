namespace Vault.App.Services;

/// <summary>
/// Hotkey flow: capture foreground window -> regex-match an item -> inject keystrokes.
/// Typing runs on a worker thread; the target window keeps focus because the
/// hotkey never activates our window.
/// </summary>
public static class AutoTypeService
{
    public static async Task<string> RunOnceAsync(VaultService vault, CancellationToken ct = default)
    {
        if (!vault.IsUnlocked)
            return "密碼庫未解鎖，Auto-Type 已略過。";

        var (title, process) = ForegroundWindow.GetActive();
        string self = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        if (!string.IsNullOrEmpty(self) &&
            string.Equals(process, self, StringComparison.OrdinalIgnoreCase))
            return "目前視窗是密碼庫本身，已略過。";
        if (string.IsNullOrWhiteSpace(title))
            return "讀不到目前視窗標題。";

        var match = vault.FindAutoTypeMatch(title, process);
        if (match is null)
            return $"找不到符合「{title}」的項目，請先在列表選取後綁定。";

        await AutoTyper.WaitForModifiersReleasedAsync(3000, ct).ConfigureAwait(false);
        await Task.Delay(150, ct).ConfigureAwait(false);
        await Task.Run(() => AutoTyper.TypeSequence(match.Sequence, match.Username, match.Password), ct)
            .ConfigureAwait(false);
        return $"已填入「{match.Name}」。";
    }
}
