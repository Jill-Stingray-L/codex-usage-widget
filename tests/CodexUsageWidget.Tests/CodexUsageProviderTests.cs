using System.Text.Json;
using CodexUsageWidget.Infrastructure.Codex;

namespace CodexUsageWidget.Tests;

public sealed class CodexUsageProviderTests
{
    [Fact]
    public async Task ReadUsageKeepsRateLimitsWhenTokenActivityFails()
    {
        await using var session = new FakeCodexAppServerSession(
            """{ "rateLimits": { "limitId": "codex", "primary": { "usedPercent": 30 } } }""",
            tokenUsageError: new InvalidOperationException("not supported"));
        await using var provider = new CodexUsageProvider(session);
        string? diagnostic = null;
        provider.DiagnosticMessage += (_, message) => diagnostic = message;

        var snapshot = await provider.ReadUsageAsync();

        Assert.Single(snapshot.GeneralWindows);
        Assert.Null(snapshot.TokenActivity);
        Assert.Contains("rate limits remain active", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderForwardsRateLimitNotificationsOnly()
    {
        await using var session = new FakeCodexAppServerSession(
            """{ "rateLimits": { "limitId": "codex", "primary": { "usedPercent": 30 } } }""");
        await using var provider = new CodexUsageProvider(session);
        var notifications = 0;
        provider.RateLimitsChanged += (_, _) => notifications++;

        session.RaiseNotification("thread/updated");
        session.RaiseNotification("account/rateLimits/updated");

        Assert.Equal(1, notifications);
    }

    private sealed class FakeCodexAppServerSession : ICodexAppServerSession
    {
        private readonly JsonElement _rateLimits;
        private readonly JsonElement _tokenUsage;
        private readonly Exception? _tokenUsageError;

        public FakeCodexAppServerSession(string rateLimits, Exception? tokenUsageError = null)
        {
            _rateLimits = ParseAndClone(rateLimits);
            _tokenUsage = ParseAndClone("""{ "summary": {} }""");
            _tokenUsageError = tokenUsageError;
        }

        public event EventHandler<string>? NotificationReceived;

        public event EventHandler<string>? DiagnosticMessage
        {
            add { }
            remove { }
        }

        public Task<JsonElement> RequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            _ = parameters;
            cancellationToken.ThrowIfCancellationRequested();
            if (method == "account/rateLimits/read")
            {
                return Task.FromResult(_rateLimits);
            }

            if (_tokenUsageError is not null)
            {
                return Task.FromException<JsonElement>(_tokenUsageError);
            }

            return Task.FromResult(_tokenUsage);
        }

        public void RaiseNotification(string method) =>
            NotificationReceived?.Invoke(this, method);

        public ValueTask DisposeAsync()
        {
            NotificationReceived = null;
            return ValueTask.CompletedTask;
        }

        private static JsonElement ParseAndClone(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
