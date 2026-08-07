using System.Diagnostics;
using CodexUsageWidget.Application;
using CodexUsageWidget.Infrastructure.Codex.Hooks;

namespace CodexUsageWidget.Tests;

public sealed class CodexActivityPipeTests
{
    [Fact]
    public async Task ServerReceivesStartAndStopSignals()
    {
        var pipeName = UniquePipeName();
        await using var source = new CodexActivityPipeSignalSource(pipeName);
        var received = new List<CodexActivitySignal>();
        var bothReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SignalReceived += signal =>
        {
            lock (received)
            {
                received.Add(signal);
                if (received.Count == 2)
                {
                    bothReceived.TrySetResult();
                }
            }
        };
        await source.StartAsync();

        Assert.True(await CodexActivityPipeClient.TrySendAsync(
            new(CodexActivitySignalKind.TurnStarted, "session", "turn"),
            pipeName));
        Assert.True(await CodexActivityPipeClient.TrySendAsync(
            new(CodexActivitySignalKind.TurnStopped, "session", "turn"),
            pipeName));
        await bothReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Collection(
            received,
            signal => Assert.Equal(CodexActivitySignalKind.TurnStarted, signal.Kind),
            signal => Assert.Equal(CodexActivitySignalKind.TurnStopped, signal.Kind));
    }

    [Fact]
    public async Task ClientWithoutServerFinishesQuickly()
    {
        var stopwatch = Stopwatch.StartNew();

        var sent = await CodexActivityPipeClient.TrySendAsync(
            new(CodexActivitySignalKind.TurnStarted, "session", "turn"),
            UniquePipeName(),
            connectTimeoutMilliseconds: 100);

        stopwatch.Stop();
        Assert.False(sent);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public async Task CancellationStopsListenLoop()
    {
        var source = new CodexActivityPipeSignalSource(UniquePipeName());
        await source.StartAsync();

        await source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static string UniquePipeName() => $"CodexUsageWidget.Tests.{Guid.NewGuid():N}";
}
