namespace CodexUsageWidget.Domain;

public sealed record UsageRateLimits(
    IReadOnlyList<UsageLimitBucket> Limits,
    string? PlanType,
    ResetCreditSummary? ResetCredits);
