namespace CodexUsageWidget.Infrastructure.Logging;

public interface IAppLogger
{
    void Info(string message);

    void LogError(string message, Exception? exception = null);
}
