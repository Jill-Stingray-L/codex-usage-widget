namespace CodexUsageWidget.Domain;

public sealed record ResetCreditSummary(
    long AvailableCount,
    IReadOnlyList<RateLimitResetCredit>? Credits);
