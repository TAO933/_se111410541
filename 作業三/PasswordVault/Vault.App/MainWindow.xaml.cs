using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Vault.App.Services;
using Vault.App.ViewModels;

namespace Vault.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly VaultService _vault;
    private readonly HotkeyService _hotkey = new();
    private bool _hotkeyStarted;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    private const int SW_MINIMIZE = 6;

    public MainWindow(VaultService vault)
    {
        InitializeComponent();
        _vault = vault;
        ViewModel = new MainViewModel(vault);
        RootPanel.DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsUnlocked))
                UpdatePanels();
        };
        ViewModel.SecretsConsumed += (_, _) =>
        {
            MasterBox.Password = "";
            NewPasswordBox.Password = "";
        };
        ViewModel.ClipboardCopied += (_, _) => Minimize();
        Activated += OnActivatedOnce;
        Closed += (_, _) => _hotkey.Dispose();
        UpdatePanels();
    }

    private void Minimize()
    {
        try
        {
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_MINIMIZE);
        }
        catch
        {
            // 最小化失敗不影響複製本身。
        }
    }

    private void OnActivatedOnce(object sender, WindowActivatedEventArgs args)
    {
        if (_hotkeyStarted)
            return;
        try
        {
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _hotkey.Start(hwnd);
            _hotkey.HotkeyPressed += async (_, _) =>
            {
                // 回到 UI 執行緒更新 Status；打字本身在 worker thread 跑，
                // 目標視窗保持前景（hotkey 不會激活本視窗）。
                DispatcherQueue.TryEnqueue(() => ViewModel.Status = "收到 Ctrl+Shift+L，填入中…放開按鍵別動滑鼠。");
                string result = await AutoTypeService.RunOnceAsync(_vault);
                DispatcherQueue.TryEnqueue(() => ViewModel.Status = result);
            };
            _hotkey.CopyHotkeyPressed += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    ViewModel.Status = "收到 Ctrl+Shift+C，複製中…";
                    await ViewModel.CopyMatchPasswordAsync();
                });
            };
            _hotkeyStarted = true;
            ViewModel.Status = "全域快捷鍵 Ctrl+Shift+L 填入 / Ctrl+Shift+C 複製已就緒。";
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"快捷鍵註冊失敗（可能被佔用）：{ex.Message}，仍可用按鈕操作。";
        }
    }

    private void UpdatePanels()
    {
        LockedPanel.Visibility = ViewModel.IsUnlocked ? Visibility.Collapsed : Visibility.Visible;
        VaultPanel.Visibility = ViewModel.IsUnlocked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMasterPasswordChanged(object sender, RoutedEventArgs e) =>
        ViewModel.MasterPassword = MasterBox.Password;

    private void OnNewPasswordChanged(object sender, RoutedEventArgs e) =>
        ViewModel.NewPassword = NewPasswordBox.Password;
}
