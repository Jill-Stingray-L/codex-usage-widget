namespace CodexUsageWidget.Domain;

public sealed record RateLimitResetCredit(
    string Id,
    string Status,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    string? Title,
    string? Description);
