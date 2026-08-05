using System.IO;

namespace CodexUsageWidget.Infrastructure.Logging;

public sealed class FileLogger : IAppLogger
{
    private readonly object _writeLock = new();
    private readonly string _logDirectory;

    public FileLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        TryDeleteExpiredLogs();
    }

    public void Info(string message) => Write("INF", message, null);

    public void LogError(string message, Exception? exception = null) => Write("ERR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(_logDirectory);
                var path = Path.Combine(
                    _logDirectory,
                    $"codex-usage-widget-{DateTime.UtcNow:yyyyMMdd}.log");
                var entry = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (exception is not null)
                {
                    entry += Environment.NewLine + exception;
                }

                File.AppendAllText(path, entry + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take down the widget.
        }
    }

    private void TryDeleteExpiredLogs()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (var path in Directory.EnumerateFiles(_logDirectory, "codex-usage-widget-*.log"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
