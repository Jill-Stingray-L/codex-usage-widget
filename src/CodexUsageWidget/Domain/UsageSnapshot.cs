namespace CodexUsageWidget.Domain;

public sealed record UsageSnapshot(
    UsageRateLimits RateLimits,
    TokenActivitySummary? TokenActivity,
    DateTimeOffset FetchedAt)
{
    public IReadOnlyList<UsageLimitBucket> GeneralLimits => RateLimits.Limits
        .Where(limit => limit.IsGeneral)
        .ToArray();

    public IReadOnlyList<UsageWindow> GeneralWindows => GeneralLimits
        .SelectMany(limit => limit.Windows)
        .OrderBy(window => window.WindowDurationMinutes ?? long.MaxValue)
        .ToArray();

    public UsageWindow? MostConstrainedWindow => GeneralWindows
        .OrderBy(window => window.RemainingPercent)
        .ThenBy(window => window.ResetsAt ?? DateTimeOffset.MaxValue)
        .FirstOrDefault();
}
