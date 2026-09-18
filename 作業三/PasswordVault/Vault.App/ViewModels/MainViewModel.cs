using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.App.Services;

namespace Vault.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly VaultService _vault;

    public MainViewModel(VaultService vault)
    {
        _vault = vault;
        HasVault = _vault.VaultExists();
    }

    public event EventHandler? SecretsConsumed;

    /// <summary>複製成功後觸發，MainWindow 接到就最小化，方便切回遊戲 Ctrl+V。</summary>
    public event EventHandler? ClipboardCopied;

    [ObservableProperty]
    public partial string ClipboardWarning { get; set; } = "";

    [ObservableProperty]
    public partial bool HasVault { get; set; }

    [ObservableProperty]
    public partial bool IsUnlocked { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string NewName { get; set; } = "";

    [ObservableProperty]
    public partial string NewUrl { get; set; } = "";

    [ObservableProperty]
    public partial string NewUsername { get; set; } = "";

    [ObservableProperty]
    public partial VaultItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial string WindowRegex { get; set; } = "";

    [ObservableProperty]
    public partial int ClipboardCountdown { get; set; }

    public string MasterPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";

    private CancellationTokenSource? _clipCts;

    public ObservableCollection<VaultItem> Items { get; } = new();

    [RelayCommand]
    private async Task UnlockAsync()
    {
        try
        {
            Status = "解鎖中…";
            await _vault.UnlockAsync(MasterPassword);
            MasterPassword = "";
            SecretsConsumed?.Invoke(this, EventArgs.Empty);
            IsUnlocked = true;
            HasVault = true;
            RefreshItems();
            RefreshClipboardWarning();
            Status = "已解鎖。";
        }
        catch (Exception ex)
        {
            Status = $"解鎖失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        try
        {
            Status = "建立中…（Argon2 約需 1 秒）";
            await _vault.CreateAsync(MasterPassword);
            MasterPassword = "";
            SecretsConsumed?.Invoke(this, EventArgs.Empty);
            IsUnlocked = true;
            HasVault = true;
            RefreshItems();
            RefreshClipboardWarning();
            Status = "密碼庫已建立並解鎖。";
        }
        catch (Exception ex)
        {
            Status = $"建立失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private void Lock()
    {
        CancelClipboardCountdown();
        ClipboardService.Clear();
        ClipboardCountdown = 0;
        _vault.Lock();
        IsUnlocked = false;
        Items.Clear();
        Status = "已鎖定，記憶體金鑰與剪貼簿已清除。";
    }

    [RelayCommand]
    private void Add()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewName) ||
                string.IsNullOrWhiteSpace(NewUsername) ||
                string.IsNullOrEmpty(NewPassword))
            {
                Status = "名稱、帳號、密碼都要填。";
                return;
            }
            _vault.Add(NewName.Trim(), NewUrl.Trim(), NewUsername.Trim(), NewPassword);
            NewName = "";
            NewUrl = "";
            NewUsername = "";
            NewPassword = "";
            SecretsConsumed?.Invoke(this, EventArgs.Empty);
            RefreshItems();
            Status = "已儲存。";
        }
        catch (Exception ex)
        {
            Status = $"儲存失敗：{ex.Message}";
        }
    }

    private void RefreshItems()
    {
        Items.Clear();
        foreach (var item in _vault.LoadItems())
            Items.Add(item);
    }

    private void RefreshClipboardWarning()
    {
        try
        {
            ClipboardWarning = ClipboardPolicy.GetWarning() ?? "";
        }
        catch
        {
            ClipboardWarning = "";
        }
    }

    partial void OnSelectedItemChanged(VaultItem? value)
    {
        if (value is null || !IsUnlocked)
        {
            WindowRegex = "";
            return;
        }
        try
        {
            WindowRegex = _vault.GetWindowRegex(value.Id);
        }
        catch (Exception ex)
        {
            Status = $"讀取綁定失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private void BindRegex()
    {
        try
        {
            if (SelectedItem is null)
            {
                Status = "請先在列表選一個項目。";
                return;
            }
            _vault.BindWindow(SelectedItem.Id, WindowRegex);
            Status = string.IsNullOrWhiteSpace(WindowRegex)
                ? $"已清除「{SelectedItem.Name}」的視窗綁定。"
                : $"已將「{SelectedItem.Name}」綁定到 /{WindowRegex}/。切到遊戲按 Ctrl+Shift+L 測試。";
        }
        catch (Exception ex)
        {
            Status = $"綁定失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CaptureWindowAsync()
    {
        try
        {
            Status = "3 秒後抓取前景視窗…請立刻切到遊戲/目標程式。";
            await Task.Delay(3000);
            var (title, process) = ForegroundWindow.GetActive();
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(process))
            {
                Status = "抓不到視窗標題，請重試。";
                return;
            }
            // 預設用 process 名精確綁定最穩，title 變化大（房名/版本號）。
            string suggestion = string.IsNullOrWhiteSpace(process)
                ? $"(?i).*{System.Text.RegularExpressions.Regex.Escape(title)}.*"
                : $"(?i).*{System.Text.RegularExpressions.Regex.Escape(process)}.*";
            WindowRegex = suggestion;
            Status = $"已抓到 title=\"{title}\" proc=\"{process}\"，已填入建議 Regex，按「綁定」儲存。";
        }
        catch (Exception ex)
        {
            Status = $"抓取失敗：{ex.Message}";
        }
    }

    private bool _autoTypeBusy;

    [RelayCommand]
    private async Task AutoTypeNowAsync()
    {
        if (_autoTypeBusy)
            return;
        _autoTypeBusy = true;
        try
        {
            Status = "Auto-Type 執行中…（先切到目標視窗再按此鈕，或直接用 Ctrl+Shift+L）";
            Status = await AutoTypeService.RunOnceAsync(_vault);
        }
        catch (Exception ex)
        {
            Status = $"Auto-Type 失敗：{ex.Message}";
        }
        finally
        {
            _autoTypeBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyUsernameAsync()
    {
        try
        {
            if (SelectedItem is null)
            {
                Status = "請先在列表選一個項目。";
                return;
            }
            string username = _vault.GetCredential(SelectedItem.Id).Username;
            await CopyWithCountdownAsync(username, $"已複製「{SelectedItem.Name}」的帳號，10 秒後清除。");
        }
        catch (Exception ex)
        {
            Status = $"複製失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyPasswordAsync()
    {
        try
        {
            if (SelectedItem is null)
            {
                Status = "請先在列表選一個項目。";
                return;
            }
            string password = _vault.GetCredential(SelectedItem.Id).Password;
            await CopyWithCountdownAsync(password, $"已複製「{SelectedItem.Name}」的密碼，10 秒後清除。切到遊戲 Ctrl+V 貼上。");
        }
        catch (Exception ex)
        {
            Status = $"複製失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearClipboard()
    {
        CancelClipboardCountdown();
        ClipboardService.Clear();
        ClipboardCountdown = 0;
        Status = "剪貼簿已清除。";
    }

    /// <summary>
    /// Ctrl+Shift+C 全域降級：用當前前景視窗找綁定項目，只複製密碼不打字。
    /// 在遊戲/反作弊視窗按，回来 Ctrl+V 即可。
    /// </summary>
    public async Task CopyMatchPasswordAsync()
    {
        try
        {
            if (!IsUnlocked)
            {
                Status = "密碼庫未解鎖，複製已略過。";
                return;
            }
            var (title, process) = ForegroundWindow.GetActive();
            var match = _vault.FindAutoTypeMatch(title ?? "", process ?? "");
            if (match is null)
            {
                Status = $"找不到符合「{title}」的項目，請先綁定。";
                return;
            }
            await CopyWithCountdownAsync(match.Password, $"已複製「{match.Name}」的密碼，10 秒後清除。切回遊戲 Ctrl+V。");
        }
        catch (Exception ex)
        {
            Status = $"複製失敗：{ex.Message}";
        }
    }

    private async Task CopyWithCountdownAsync(string text, string doneMessage)
    {
        CancelClipboardCountdown();
        ClipboardService.Copy(text);
        RefreshClipboardWarning();
        string warn = string.IsNullOrEmpty(ClipboardWarning) ? "" : " " + ClipboardWarning;
        Status = doneMessage + warn;
        ClipboardCopied?.Invoke(this, EventArgs.Empty);
        var cts = new CancellationTokenSource();
        _clipCts = cts;
        try
        {
            for (int i = 10; i > 0; i--)
            {
                ClipboardCountdown = i;
                await Task.Delay(1000, cts.Token);
            }
            bool cleared = await ClipboardService.ClearIfMatchesAsync(text);
            ClipboardCountdown = 0;
            if (!cts.IsCancellationRequested)
                Status = cleared ? "剪貼簿已自動清除。" : "剪貼簿已保留（你之後又複製了別的東西）。";
        }
        catch (TaskCanceledException)
        {
            // 被新的複製或鎖定取代，不動 Status（新任務會設）。
        }
        finally
        {
            if (_clipCts == cts)
                _clipCts = null;
        }
    }

    private void CancelClipboardCountdown()
    {
        try { _clipCts?.Cancel(); } catch { }
        _clipCts = null;
    }
}
