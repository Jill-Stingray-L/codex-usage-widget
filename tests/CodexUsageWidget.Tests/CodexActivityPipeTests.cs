using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using CodexUsageWidget.Application;
using CodexUsageWidget.Infrastructure.Codex.Hooks;

namespace CodexUsageWidget.Tests;

public sealed class CodexActivityPipeTests
{
    [Fact]
    public async Task AcceptedStartCannotBeEmittedAfterLaterStop()
    {
        var pipeName = UniquePipeName();
        var source = new CodexActivityPipeSignalSource(pipeName);
        await using var monitor = new CodexActivityMonitor(source);
        var received = new List<CodexActivitySignal>();
        var activityChanges = new List<bool>();
        var firstReceived = new TaskCompletionSource<CodexActivitySignal>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bothReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SignalReceived += signal =>
        {
            lock (received)
            {
                received.Add(signal);
                firstReceived.TrySetResult(signal);
                if (received.Count == 2)
                {
                    bothReceived.TrySetResult();
                }
            }
        };
        monitor.ActivityChanged += activityChanges.Add;
        await monitor.StartAsync();

        using var startClient = CreateClient(pipeName);
        await startClient.ConnectAsync(1000);
        using var stopClient = CreateClient(pipeName);
        await stopClient.ConnectAsync(1000);

        await WriteSignalAsync(
            stopClient,
            new(CodexActivitySignalKind.TurnStopped, "session", "turn"));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            firstReceived.Task.WaitAsync(TimeSpan.FromMilliseconds(250)));

        await WriteSignalAsync(
            startClient,
            new(CodexActivitySignalKind.TurnStarted, "session", "turn"));
        await bothReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Collection(
            received,
            signal => Assert.Equal(CodexActivitySignalKind.TurnStarted, signal.Kind),
            signal => Assert.Equal(CodexActivitySignalKind.TurnStopped, signal.Kind));
        Assert.Equal([true, false], activityChanges);
        Assert.False(monitor.IsActive);
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
    public async Task StalledAcceptedClientCannotBlockLaterSignalsIndefinitely()
    {
        var pipeName = UniquePipeName();
        await using var source = new CodexActivityPipeSignalSource(
            pipeName,
            readTimeoutMilliseconds: 100);
        var received = new TaskCompletionSource<CodexActivitySignal>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SignalReceived += signal => received.TrySetResult(signal);
        await source.StartAsync();

        using var stalledClient = CreateClient(pipeName);
        await stalledClient.ConnectAsync(1000);
        using var laterClient = CreateClient(pipeName);
        await laterClient.ConnectAsync(1000);
        var expected = new CodexActivitySignal(
            CodexActivitySignalKind.TurnStarted,
            "later-session",
            "later-turn");

        await WriteSignalAsync(laterClient, expected);

        Assert.Equal(expected, await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task CancellationStopsListenLoop()
    {
        var source = new CodexActivityPipeSignalSource(UniquePipeName());
        await source.StartAsync();

        await source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ShutdownCancelsAStalledAcceptedClient()
    {
        var pipeName = UniquePipeName();
        var source = new CodexActivityPipeSignalSource(
            pipeName,
            readTimeoutMilliseconds: 10_000);
        await source.StartAsync();
        using var stalledClient = CreateClient(pipeName);
        await stalledClient.ConnectAsync(1000);

        await source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static string UniquePipeName() => $"CodexUsageWidget.Tests.{Guid.NewGuid():N}";

    private static NamedPipeClientStream CreateClient(string pipeName) => new(
        ".",
        pipeName,
        PipeDirection.Out,
        PipeOptions.Asynchronous);

    private static async Task WriteSignalAsync(
        Stream pipe,
        CodexActivitySignal signal)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(signal);
        var lengthPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);
        await pipe.WriteAsync(lengthPrefix);
        await pipe.WriteAsync(payload);
        await pipe.FlushAsync();
    }
}
