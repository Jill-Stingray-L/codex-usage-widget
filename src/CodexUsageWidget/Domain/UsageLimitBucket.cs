namespace CodexUsageWidget.Domain;

public sealed record UsageLimitBucket(
    string LimitId,
    string Label,
    bool IsGeneral,
    IReadOnlyList<UsageWindow> Windows,
    CreditBalance? Credits,
    SpendLimit? IndividualLimit,
    string? ReachedState,
    bool? SpendControlReached);
