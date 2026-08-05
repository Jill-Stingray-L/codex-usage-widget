using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CodexUsageWidget.Models;

namespace CodexUsageWidget.Services;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _readLoop;
    private long _nextRequestId;
    private int _refreshNotificationActive;

    public event EventHandler? RateLimitsChanged;
    public event EventHandler<string>? DiagnosticMessage;

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        var startInfo = BuildStartInfo();
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            var message = process.ExitCode == 0
                ? "Codex app-server stopped."
                : $"Codex app-server exited with code {process.ExitCode}.";
            DiagnosticMessage?.Invoke(this, message);
            FailPending(new InvalidOperationException(message));
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start codex app-server.");
        }

        _process = process;
        _stdin = process.StandardInput;
        _stdin.AutoFlush = true;
        _readLoop = ReadLoopAsync(process.StandardOutput, _lifetime.Token);
        _ = ReadDiagnosticsAsync(process.StandardError, _lifetime.Token);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "codex_usage_widget",
                    title = "Codex Usage Widget",
                    version = "0.1.0"
                },
                capabilities = new
                {
                    optOutNotificationMethods = Array.Empty<string>()
                }
            },
            timeout.Token);

        await SendNotificationAsync("initialized", new { }, timeout.Token);
    }

    public async Task<UsageSnapshot> ReadUsageAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken);
        var result = await SendRequestAsync("account/rateLimits/read", null, cancellationToken);
        return ParseUsageSnapshot(result);
    }

    private static ProcessStartInfo BuildStartInfo()
    {
        var executable = ResolveCodexExecutable();
        ProcessStartInfo info;

        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            info = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
            };
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("call");
            info.ArgumentList.Add(executable);
            info.ArgumentList.Add("app-server");
        }
        else
        {
            info = new ProcessStartInfo { FileName = executable };
            info.ArgumentList.Add("app-server");
        }

        info.UseShellExecute = false;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        info.StandardOutputEncoding = System.Text.Encoding.UTF8;
        info.StandardErrorEncoding = System.Text.Encoding.UTF8;
        return info;
    }

    private static string ResolveCodexExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "codex.cmd", "codex.exe", "codex.bat" }
            : new[] { "codex" };

        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var candidate in candidates)
        {
            foreach (var directory in directories)
            {
                try
                {
                    var fullPath = Path.Combine(directory.Trim(), candidate);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries and continue searching.
                }
            }
        }

        return "codex";
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Could not track the Codex request.");
        }

        try
        {
            var payload = parameters is null
                ? new { method, id }
                : (object)new { method, id, @params = parameters };
            await WriteMessageAsync(payload, cancellationToken);

            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<JsonElement>)state!).TrySetCanceled(),
                completion);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteMessageAsync(object payload, CancellationToken cancellationToken)
    {
        var writer = _stdin ?? throw new InvalidOperationException("Codex app-server is not running.");
        var json = JsonSerializer.Serialize(payload);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    DiagnosticMessage?.Invoke(this, $"Ignored non-JSON app-server output: {line}");
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) &&
                        _pending.TryGetValue(id, out var completion))
                    {
                        if (root.TryGetProperty("error", out var error))
                        {
                            completion.TrySetException(new InvalidOperationException(FormatRpcError(error)));
                        }
                        else if (root.TryGetProperty("result", out var result))
                        {
                            completion.TrySetResult(result.Clone());
                        }

                        continue;
                    }

                    if (root.TryGetProperty("method", out var methodElement) &&
                        methodElement.GetString() == "account/rateLimits/updated")
                    {
                        QueueRateLimitsChanged();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DiagnosticMessage?.Invoke(this, ex.Message);
            FailPending(ex);
        }
    }

    private async Task ReadDiagnosticsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    DiagnosticMessage?.Invoke(this, line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void QueueRateLimitsChanged()
    {
        if (Interlocked.Exchange(ref _refreshNotificationActive, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, _lifetime.Token);
                RateLimitsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _refreshNotificationActive, 0);
            }
        });
    }

    private static UsageSnapshot ParseUsageSnapshot(JsonElement result)
    {
        var windows = new List<UsageWindow>();
        string? planType = null;

        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in buckets.EnumerateObject())
            {
                ParseBucket(property.Value, property.Name, windows, ref planType);
            }
        }

        if (windows.Count == 0 && result.TryGetProperty("rateLimits", out var legacyBucket) &&
            legacyBucket.ValueKind == JsonValueKind.Object)
        {
            ParseBucket(legacyBucket, "codex", windows, ref planType);
        }

        var ordered = windows
            .OrderBy(window => window.WindowDurationMinutes ?? int.MaxValue)
            .ThenBy(window => window.LimitId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new UsageSnapshot(ordered, planType, DateTimeOffset.Now);
    }

    private static void ParseBucket(
        JsonElement bucket,
        string fallbackId,
        ICollection<UsageWindow> destination,
        ref string? planType)
    {
        var limitId = ReadString(bucket, "limitId") ?? fallbackId;
        var limitName = ReadString(bucket, "limitName");
        planType ??= ReadString(bucket, "planType");

        if (IsExcludedLimit(limitId, limitName))
        {
            return;
        }

        AddWindow(bucket, "primary", limitId, limitName, destination);
        AddWindow(bucket, "secondary", limitId, limitName, destination);
    }

    private static bool IsExcludedLimit(string limitId, string? limitName) =>
        limitId.Contains("spark", StringComparison.OrdinalIgnoreCase) ||
        (limitName?.Contains("spark", StringComparison.OrdinalIgnoreCase) ?? false);

    private static void AddWindow(
        JsonElement bucket,
        string windowKey,
        string limitId,
        string? limitName,
        ICollection<UsageWindow> destination)
    {
        if (!bucket.TryGetProperty(windowKey, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usedElement) || !usedElement.TryGetDouble(out var used))
        {
            return;
        }

        int? duration = null;
        if (window.TryGetProperty("windowDurationMins", out var durationElement) &&
            durationElement.TryGetInt32(out var durationValue))
        {
            duration = durationValue;
        }

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var resetElement) && resetElement.TryGetInt64(out var unixSeconds))
        {
            try
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        var label = BuildWindowLabel(limitName, limitId, duration, windowKey);
        destination.Add(new UsageWindow(limitId, label, Math.Clamp(used, 0d, 100d), duration, resetsAt));
    }

    private static string BuildWindowLabel(string? limitName, string limitId, int? duration, string windowKey)
    {
        var durationLabel = duration switch
        {
            >= 10_000 => "Weekly window",
            >= 1_440 when duration % 1_440 == 0 => $"{duration / 1_440}d window",
            >= 60 when duration % 60 == 0 => $"{duration / 60}h window",
            > 0 => $"{duration}m window",
            _ => windowKey == "primary" ? "Primary window" : "Secondary window"
        };

        var bucketLabel = string.IsNullOrWhiteSpace(limitName) ||
                          string.Equals(limitName, "codex", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(limitId, "codex", StringComparison.OrdinalIgnoreCase)
            ? null
            : limitName.Replace('_', ' ');

        return bucketLabel is null
            ? durationLabel
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(bucketLabel) + " · " + durationLabel;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string FormatRpcError(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "unknown";
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : error.ToString();
        return $"Codex app-server error {code}: {message}";
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        FailPending(new OperationCanceledException("Codex usage client is shutting down."));

        try
        {
            if (_stdin is not null)
            {
                await _stdin.DisposeAsync();
            }
        }
        catch
        {
        }

        if (_process is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch
            {
            }
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop;
            }
            catch
            {
            }
        }

        _process?.Dispose();
        _writeLock.Dispose();
        _lifetime.Dispose();
    }
}
