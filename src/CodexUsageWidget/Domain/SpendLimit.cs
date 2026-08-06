namespace CodexUsageWidget.Domain;

public sealed record SpendLimit(
    string Used,
    string Limit,
    double RemainingPercent,
    DateTimeOffset ResetsAt);
