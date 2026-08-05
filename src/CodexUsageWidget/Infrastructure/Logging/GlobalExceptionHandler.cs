using System.Windows.Threading;

namespace CodexUsageWidget.Infrastructure.Logging;

public sealed class GlobalExceptionHandler : IDisposable
{
    private readonly System.Windows.Application _application;
    private readonly IAppLogger _logger;

    public GlobalExceptionHandler(System.Windows.Application application, IAppLogger logger)
    {
        _application = application;
        _logger = logger;
        _application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        _logger.LogError("Unhandled UI exception.", e.Exception);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _logger.LogError("Unhandled application exception.", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError("Unobserved background task exception.", e.Exception);
        e.SetObserved();
    }

    public void Dispose()
    {
        _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }
}
