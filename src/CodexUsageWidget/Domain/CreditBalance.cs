namespace CodexUsageWidget.Domain;

public sealed record CreditBalance(
    bool HasCredits,
    bool Unlimited,
    string? Balance);
