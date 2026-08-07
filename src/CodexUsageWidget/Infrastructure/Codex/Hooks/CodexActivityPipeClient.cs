using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using CodexUsageWidget.Application;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public static class CodexActivityPipeClient
{
    private const int MaximumPayloadBytes = 4096;
    public const string DefaultPipeName = "CodexUsageWidget.Activity.v1";
    public const int DefaultConnectTimeoutMilliseconds = 150;

    public static async Task<bool> TrySendAsync(
        CodexActivitySignal signal,
        string pipeName = DefaultPipeName,
        int connectTimeoutMilliseconds = DefaultConnectTimeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(signal);
            if (payload.Length > MaximumPayloadBytes)
            {
                return false;
            }

            var lengthPrefix = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(connectTimeoutMilliseconds, cancellationToken)
                .ConfigureAwait(false);
            await pipe.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
