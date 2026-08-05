namespace CodexUsageWidget.Domain;

public sealed record UsageWindow(
    string LimitId,
    string Label,
    double UsedPercent,
    int? WindowDurationMinutes,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);
}
