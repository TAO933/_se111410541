using Microsoft.UI.Xaml;
using Vault.App.Services;

namespace Vault.App;

public partial class App : Application
{
    public VaultService Vault { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        m_pipe = new PipeServer(Vault);
        m_pipe.Start();
        m_window = new MainWindow(Vault);
        m_window.Activate();
    }

    private Window? m_window;
    private PipeServer? m_pipe;
}
