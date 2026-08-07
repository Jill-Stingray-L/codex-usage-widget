using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using CodexUsageWidget.Application;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public sealed class CodexActivityPipeSignalSource : ICodexActivitySignalSource
{
    private const int MaximumPayloadBytes = 4096;
    private const int MaximumIdentifierLength = 256;

    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _clientTasksLock = new();
    private readonly HashSet<Task> _clientTasks = [];
    private Task? _listenTask;

    public CodexActivityPipeSignalSource(string pipeName = CodexActivityPipeClient.DefaultPipeName)
    {
        _pipeName = pipeName;
    }

    public event Action<CodexActivitySignal>? SignalReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _listenTask ??= ListenAsync(_lifetime.Token);
        return Task.CompletedTask;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pipe = CreateServer();
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    pipe.Dispose();
                    throw;
                }

                TrackClientTask(HandleClientAsync(pipe, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private NamedPipeServerStream CreateServer() => new(
        _pipeName,
        PipeDirection.In,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
        MaximumPayloadBytes,
        MaximumPayloadBytes);

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using (pipe)
        {
            try
            {
                var signal = await ReadSignalAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (signal is not null)
                {
                    SignalReceived?.Invoke(signal);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private void TrackClientTask(Task task)
    {
        lock (_clientTasksLock)
        {
            _clientTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_clientTasksLock)
                {
                    _clientTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task<CodexActivitySignal?> ReadSignalAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var lengthPrefix = new byte[sizeof(int)];
        try
        {
            await input.ReadExactlyAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            return null;
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (payloadLength <= 0 || payloadLength > MaximumPayloadBytes)
        {
            return null;
        }

        var payload = new byte[payloadLength];
        try
        {
            await input.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            var signal = JsonSerializer.Deserialize<CodexActivitySignal>(payload);
            return IsValid(signal) ? signal : null;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or JsonException)
        {
            return null;
        }
    }

    private static bool IsValid(CodexActivitySignal? signal) =>
        signal is not null &&
        !string.IsNullOrWhiteSpace(signal.SessionId) &&
        signal.SessionId.Length <= MaximumIdentifierLength &&
        (signal.Kind == CodexActivitySignalKind.SessionEnded ||
         (!string.IsNullOrWhiteSpace(signal.TurnId) &&
          signal.TurnId.Length <= MaximumIdentifierLength));

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_listenTask is not null)
        {
            await _listenTask.ConfigureAwait(false);
        }

        Task[] clientTasks;
        lock (_clientTasksLock)
        {
            clientTasks = [.. _clientTasks];
        }

        await Task.WhenAll(clientTasks).ConfigureAwait(false);

        _lifetime.Dispose();
    }
}
