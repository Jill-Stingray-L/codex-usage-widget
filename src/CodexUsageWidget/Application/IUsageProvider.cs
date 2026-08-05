using CodexUsageWidget.Domain;

namespace CodexUsageWidget.Application;

public interface IUsageProvider : IAsyncDisposable
{
    event EventHandler? RateLimitsChanged;

    event EventHandler<string>? DiagnosticMessage;

    Task<UsageSnapshot> ReadUsageAsync(CancellationToken cancellationToken = default);
}
