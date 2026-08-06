namespace CodexUsageWidget.Domain;

public sealed record UsageWindow(
    string Label,
    double UsedPercent,
    long? WindowDurationMinutes,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);
}
