namespace CodexUsageWidget.Domain;

public sealed record UsageSnapshot(
    IReadOnlyList<UsageWindow> Windows,
    string? PlanType,
    DateTimeOffset FetchedAt)
{
    public UsageWindow? Primary => Windows.Count > 0 ? Windows[0] : null;
}
