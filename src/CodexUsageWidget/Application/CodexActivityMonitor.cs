namespace CodexUsageWidget.Application;

public sealed class CodexActivityMonitor : IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly ICodexActivitySignalSource _source;
    private readonly HashSet<ActiveTurn> _activeTurns = [];
    private bool _started;

    public CodexActivityMonitor(ICodexActivitySignalSource source)
    {
        _source = source;
    }

    public event Action<bool>? ActivityChanged;

    public bool IsActive
    {
        get
        {
            lock (_stateLock)
            {
                return _activeTurns.Count > 0;
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
        bool? changedState = null;
        lock (_stateLock)
        {
            var wasActive = _activeTurns.Count > 0;
            switch (signal.Kind)
            {
                case CodexActivitySignalKind.TurnStarted when signal.TurnId is not null:
                    _activeTurns.Add(new ActiveTurn(signal.SessionId, signal.TurnId));
                    break;
                case CodexActivitySignalKind.TurnStopped when signal.TurnId is not null:
                    _activeTurns.Remove(new ActiveTurn(signal.SessionId, signal.TurnId));
                    break;
                case CodexActivitySignalKind.SessionEnded:
                    _activeTurns.RemoveWhere(turn =>
                        string.Equals(turn.SessionId, signal.SessionId, StringComparison.Ordinal));
                    break;
            }

            var currentActivity = _activeTurns.Count > 0;
            if (wasActive != currentActivity)
            {
                changedState = currentActivity;
            }
        }

        if (changedState is { } emittedActivity)
        {
            ActivityChanged?.Invoke(emittedActivity);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _source.SignalReceived -= SourceOnSignalReceived;
        await _source.DisposeAsync().ConfigureAwait(false);
    }

    private readonly record struct ActiveTurn(string SessionId, string TurnId);
}
