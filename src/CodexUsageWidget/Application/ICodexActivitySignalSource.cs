namespace CodexUsageWidget.Application;

public interface ICodexActivitySignalSource : IAsyncDisposable
{
    event Action<CodexActivitySignal>? SignalReceived;

    Task StartAsync(CancellationToken cancellationToken = default);
}
