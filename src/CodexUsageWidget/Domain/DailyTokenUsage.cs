namespace CodexUsageWidget.Domain;

public sealed record DailyTokenUsage(
    DateOnly Date,
    long Tokens);
