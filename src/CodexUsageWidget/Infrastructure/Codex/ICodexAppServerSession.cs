using System.Text.Json;

namespace CodexUsageWidget.Infrastructure.Codex;

public interface ICodexAppServerSession : IAsyncDisposable
{
    event EventHandler<string>? NotificationReceived;

    event EventHandler<string>? DiagnosticMessage;

    Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken);
}
