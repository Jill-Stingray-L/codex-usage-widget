namespace CodexUsageWidget.Application;

public static class UsageTextFormatter
{
    public static string ToFriendlyError(string message)
    {
        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex CLI was not found on PATH.";
        }

        if (message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Run codex login, then refresh.";
        }

        return message.Length > 100 ? message[..100] + "…" : message;
    }

    public static string FormatReset(DateTimeOffset reset, DateTimeOffset? now = null)
    {
        var remaining = reset - (now ?? DateTimeOffset.Now);
        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        if (remaining < TimeSpan.FromHours(24))
        {
            return $"in {Math.Max(1, (int)Math.Ceiling(remaining.TotalHours))}h · {reset:HH:mm}";
        }

        return $"{reset:ddd HH:mm}";
    }

    public static string ColorForRemaining(double remainingPercent) => remainingPercent switch
    {
        <= 10 => "#F07070",
        <= 25 => "#F0B35E",
        _ => "#65D892"
    };
}
