using System.Windows;
using CodexUsageWidget.Application;
using CodexUsageWidget.Infrastructure;
using CodexUsageWidget.Infrastructure.Codex;
using CodexUsageWidget.Infrastructure.Logging;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Windows;
using CodexUsageWidget.Views;

namespace CodexUsageWidget;

public partial class App : System.Windows.Application, IDisposable
{
    private const string SingleInstanceMutexName = @"Local\CodexUsageWidget.SingleInstance";
    private SingleInstanceGuard? _singleInstanceGuard;
    private FileLogger? _logger;
    private GlobalExceptionHandler? _exceptionHandler;
    private bool _disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceGuard = SingleInstanceGuard.TryAcquire(SingleInstanceMutexName);
        if (_singleInstanceGuard is null)
        {
            Shutdown();
            return;
        }

        _logger = new FileLogger(AppPaths.LogDirectory);
        _exceptionHandler = new GlobalExceptionHandler(this, _logger);

        try
        {
            var usageProvider = new CodexUsageProvider(new CodexAppServerSession());
            var usageMonitor = new UsageMonitor(usageProvider);
            usageMonitor.DiagnosticMessage += (_, message) => _logger.Info(message);

            var window = new MainWindow(
                usageMonitor,
                new DisplayModeStore(),
                new WidgetDensityStore(),
                new TrayIconService());
            MainWindow = window;
            window.Show();
            if (window.StartsInTaskbarIndicatorMode)
            {
                window.Hide();
            }

            _logger.Info("Codex Usage Widget started.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Application startup failed.", ex);
            System.Windows.MessageBox.Show(
                "Codex Usage Widget could not start. See the log under " + AppPaths.LogDirectory,
                "Codex Usage Widget",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Codex Usage Widget stopped.");
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _exceptionHandler?.Dispose();
        _exceptionHandler = null;
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        GC.SuppressFinalize(this);
    }
}
