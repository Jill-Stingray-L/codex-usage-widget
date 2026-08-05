using System.Windows;

namespace CodexUsageWidget;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\CodexUsageWidget.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var isFirstInstance);

        if (!isFirstInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (window.StartsInTaskbarIndicatorMode)
        {
            window.Hide();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
