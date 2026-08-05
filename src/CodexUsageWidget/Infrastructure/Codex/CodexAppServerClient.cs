using System.Text.Json;
using CodexUsageWidget.Application;
using CodexUsageWidget.Domain;

namespace CodexUsageWidget.Infrastructure.Codex;

public sealed class CodexAppServerClient : IUsageProvider
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private JsonRpcConnection? _connection;
    private bool _disposed;

    public event EventHandler? RateLimitsChanged;

    public event EventHandler<string>? DiagnosticMessage;

    public async Task<UsageSnapshot> ReadUsageAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var result = await connection.RequestAsync(
                "account/rateLimits/read",
                parameters: null,
                cancellationToken)
            .ConfigureAwait(false);
        return CodexUsageParser.Parse(result);
    }

    private async Task<JsonRpcConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection is { IsRunning: true } activeConnection)
        {
            return activeConnection;
        }

        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsRunning: true } connectionStartedByAnotherRequest)
            {
                return connectionStartedByAnotherRequest;
            }

            await DisposeConnectionAsync().ConfigureAwait(false);
            var connection = CreateConnection();
            try
            {
                connection.Start();
                using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await InitializeAsync(connection, startupTimeout.Token).ConfigureAwait(false);
                _connection = connection;
                return connection;
            }
            catch
            {
                Unsubscribe(connection);
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    private JsonRpcConnection CreateConnection()
    {
        var connection = new JsonRpcConnection();
        connection.NotificationReceived += ConnectionOnNotificationReceived;
        connection.DiagnosticMessage += ConnectionOnDiagnosticMessage;
        return connection;
    }

    private static async Task InitializeAsync(
        JsonRpcConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.RequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "codex_usage_widget",
                        title = "Codex Usage Widget",
                        version = "1.0.0"
                    },
                    capabilities = new
                    {
                        optOutNotificationMethods = Array.Empty<string>()
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

        await connection.NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
    }

    private void ConnectionOnNotificationReceived(object? sender, string method)
    {
        if (method == "account/rateLimits/updated")
        {
            RateLimitsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ConnectionOnDiagnosticMessage(object? sender, string message) =>
        DiagnosticMessage?.Invoke(this, message);

    private async Task DisposeConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        var connection = _connection;
        _connection = null;
        Unsubscribe(connection);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void Unsubscribe(JsonRpcConnection connection)
    {
        connection.NotificationReceived -= ConnectionOnNotificationReceived;
        connection.DiagnosticMessage -= ConnectionOnDiagnosticMessage;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _startLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            _startLock.Release();
            _startLock.Dispose();
        }
    }
}
