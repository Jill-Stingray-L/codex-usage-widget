namespace CodexUsageWidget.Application;

public sealed class CodexActivityMonitor : IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly object _transitionLock = new();
    private readonly ICodexActivitySignalSource _source;
    private readonly Dictionary<string, string> _activeTurnsBySession =
        new(StringComparer.Ordinal);
    private bool _started;

    public CodexActivityMonitor(ICodexActivitySignalSource source)
    {
        _source = source;
    }

    public event Action<bool>? ActivityChanged;

    public event EventHandler<string>? DiagnosticMessage;

    public bool IsActive
    {
        get
        {
            lock (_stateLock)
            {
                return _activeTurnsBySession.Count > 0;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _source.SignalReceived += SourceOnSignalReceived;
        await _source.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private void SourceOnSignalReceived(CodexActivitySignal signal)
    {
        lock (_transitionLock)
        {
            bool? changedState = null;
            var recoveredOrphan = false;
            lock (_stateLock)
            {
                var wasActive = _activeTurnsBySession.Count > 0;
                switch (signal.Kind)
                {
                    case CodexActivitySignalKind.TurnStarted when signal.TurnId is not null:
                        recoveredOrphan = _activeTurnsBySession.TryGetValue(
                            signal.SessionId,
                            out var previousTurnId) &&
                            !string.Equals(previousTurnId, signal.TurnId, StringComparison.Ordinal);
                        _activeTurnsBySession[signal.SessionId] = signal.TurnId;
                        break;
                    case CodexActivitySignalKind.TurnStopped when signal.TurnId is not null:
                        if (_activeTurnsBySession.TryGetValue(signal.SessionId, out var activeTurnId) &&
                            string.Equals(activeTurnId, signal.TurnId, StringComparison.Ordinal))
                        {
                            _activeTurnsBySession.Remove(signal.SessionId);
                        }

                        break;
                    case CodexActivitySignalKind.SessionEnded:
                        _activeTurnsBySession.Remove(signal.SessionId);
                        break;
                }

                var currentActivity = _activeTurnsBySession.Count > 0;
                if (wasActive != currentActivity)
                {
                    changedState = currentActivity;
                }
            }

            if (recoveredOrphan)
            {
                DiagnosticMessage?.Invoke(
                    this,
                    "Recovered stale Codex activity state for one session.");
            }

            if (changedState is { } emittedActivity)
            {
                ActivityChanged?.Invoke(emittedActivity);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _source.SignalReceived -= SourceOnSignalReceived;
        await _source.DisposeAsync().ConfigureAwait(false);
    }
}
