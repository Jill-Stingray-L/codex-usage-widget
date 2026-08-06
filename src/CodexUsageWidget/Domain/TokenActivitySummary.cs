namespace CodexUsageWidget.Domain;

public sealed record TokenActivitySummary(
    long? LifetimeTokens,
    long? PeakDailyTokens,
    long? LongestRunningTurnSeconds,
    long? CurrentStreakDays,
    long? LongestStreakDays,
    IReadOnlyList<DailyTokenUsage> DailyUsage);
