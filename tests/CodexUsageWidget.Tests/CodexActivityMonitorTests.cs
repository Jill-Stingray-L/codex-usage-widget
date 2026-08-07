using CodexUsageWidget.Application;

namespace CodexUsageWidget.Tests;

public sealed class CodexActivityMonitorTests
{
    [Fact]
    public async Task FirstStartEnablesActivityAndDuplicateStartIsIdempotent()
    {
        await using var source = new FakeSignalSource();
        await using var monitor = new CodexActivityMonitor(source);
        var changes = new List<bool>();
        monitor.ActivityChanged += changes.Add;
        await monitor.StartAsync();

        var start = new CodexActivitySignal(CodexActivitySignalKind.TurnStarted, "session", "turn");
        source.Publish(start);
        source.Publish(start);

        Assert.True(monitor.IsActive);
        Assert.Equal([true], changes);
    }

    [Fact]
    public async Task ParallelTurnsStayActiveUntilBothStop()
    {
        await using var source = new FakeSignalSource();
        await using var monitor = new CodexActivityMonitor(source);
        var changes = new List<bool>();
        monitor.ActivityChanged += changes.Add;
        await monitor.StartAsync();

        source.Publish(new(CodexActivitySignalKind.TurnStarted, "session-a", "turn-a"));
        source.Publish(new(CodexActivitySignalKind.TurnStarted, "session-b", "turn-b"));
        source.Publish(new(CodexActivitySignalKind.TurnStopped, "session-a", "turn-a"));

        Assert.True(monitor.IsActive);
        Assert.Equal([true], changes);

        source.Publish(new(CodexActivitySignalKind.TurnStopped, "session-b", "turn-b"));

        Assert.False(monitor.IsActive);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public async Task UnknownStopIsNoOp()
    {
        await using var source = new FakeSignalSource();
        await using var monitor = new CodexActivityMonitor(source);
        var changes = new List<bool>();
        monitor.ActivityChanged += changes.Add;
        await monitor.StartAsync();

        source.Publish(new(CodexActivitySignalKind.TurnStopped, "unknown", "unknown"));

        Assert.False(monitor.IsActive);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task SessionEndRemovesOnlyTurnsFromThatSession()
    {
        await using var source = new FakeSignalSource();
        await using var monitor = new CodexActivityMonitor(source);
        var changes = new List<bool>();
        monitor.ActivityChanged += changes.Add;
        await monitor.StartAsync();

        source.Publish(new(CodexActivitySignalKind.TurnStarted, "session-a", "turn-a1"));
        source.Publish(new(CodexActivitySignalKind.TurnStarted, "session-a", "turn-a2"));
        source.Publish(new(CodexActivitySignalKind.TurnStarted, "session-b", "turn-b"));
        source.Publish(new(CodexActivitySignalKind.SessionEnded, "session-a"));

        Assert.True(monitor.IsActive);
        Assert.Equal([true], changes);

        source.Publish(new(CodexActivitySignalKind.SessionEnded, "session-b"));

        Assert.False(monitor.IsActive);
        Assert.Equal([true, false], changes);
    }

    private sealed class FakeSignalSource : ICodexActivitySignalSource
    {
        public event Action<CodexActivitySignal>? SignalReceived;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Publish(CodexActivitySignal signal) => SignalReceived?.Invoke(signal);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
