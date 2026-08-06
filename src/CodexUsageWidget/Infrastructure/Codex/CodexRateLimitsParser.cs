using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Domain;

namespace CodexUsageWidget.Infrastructure.Codex;

public static class CodexRateLimitsParser
{
    public static UsageRateLimits Parse(JsonElement result)
    {
        var limits = new List<UsageLimitBucket>();
        string? planType = null;

        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in buckets.EnumerateObject())
            {
                limits.Add(ParseBucket(property.Value, property.Name, ref planType));
            }
        }

        if (limits.Count == 0 &&
            result.TryGetProperty("rateLimits", out var legacyBucket) &&
            legacyBucket.ValueKind == JsonValueKind.Object)
        {
            limits.Add(ParseBucket(legacyBucket, "codex", ref planType));
        }

        return new UsageRateLimits(
            limits
                .OrderByDescending(limit => limit.IsGeneral)
                .ThenBy(limit => limit.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            planType,
            ParseResetCredits(result));
    }

    private static UsageLimitBucket ParseBucket(
        JsonElement bucket,
        string fallbackId,
        ref string? planType)
    {
        var limitId = ReadString(bucket, "limitId") ?? fallbackId;
        var limitName = ReadString(bucket, "limitName");
        planType ??= ReadString(bucket, "planType");

        var windows = new List<UsageWindow>();
        AddWindow(bucket, "primary", windows);
        AddWindow(bucket, "secondary", windows);

        return new UsageLimitBucket(
            limitId,
            BuildBucketLabel(limitId, limitName),
            string.Equals(limitId, "codex", StringComparison.OrdinalIgnoreCase),
            windows
                .OrderBy(window => window.WindowDurationMinutes ?? long.MaxValue)
                .ToArray(),
            ParseCredits(bucket),
            ParseSpendLimit(bucket),
            ReadString(bucket, "rateLimitReachedType"),
            ReadBoolean(bucket, "spendControlReached"));
    }

    private static void AddWindow(
        JsonElement bucket,
        string windowKey,
        List<UsageWindow> destination)
    {
        if (!bucket.TryGetProperty(windowKey, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usedElement) ||
            !usedElement.TryGetDouble(out var used))
        {
            return;
        }

        var duration = ReadInt64(window, "windowDurationMins");
        destination.Add(new UsageWindow(
            BuildWindowLabel(duration, windowKey),
            Math.Clamp(used, 0d, 100d),
            duration,
            ReadUnixTime(window, "resetsAt")));
    }

    private static CreditBalance? ParseCredits(JsonElement bucket)
    {
        if (!bucket.TryGetProperty("credits", out var credits) ||
            credits.ValueKind != JsonValueKind.Object ||
            ReadBoolean(credits, "hasCredits") is not { } hasCredits ||
            ReadBoolean(credits, "unlimited") is not { } unlimited)
        {
            return null;
        }

        return new CreditBalance(hasCredits, unlimited, ReadString(credits, "balance"));
    }

    private static SpendLimit? ParseSpendLimit(JsonElement bucket)
    {
        if (!bucket.TryGetProperty("individualLimit", out var limit) ||
            limit.ValueKind != JsonValueKind.Object ||
            ReadString(limit, "used") is not { } used ||
            ReadString(limit, "limit") is not { } maximum ||
            ReadInt64(limit, "remainingPercent") is not { } remaining ||
            ReadUnixTime(limit, "resetsAt") is not { } resetsAt)
        {
            return null;
        }

        return new SpendLimit(used, maximum, Math.Clamp(remaining, 0d, 100d), resetsAt);
    }

    private static ResetCreditSummary? ParseResetCredits(JsonElement result)
    {
        if (!result.TryGetProperty("rateLimitResetCredits", out var summary) ||
            summary.ValueKind != JsonValueKind.Object ||
            ReadInt64(summary, "availableCount") is not { } count)
        {
            return null;
        }

        IReadOnlyList<RateLimitResetCredit>? details = null;
        if (summary.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Array)
        {
            details = credits.EnumerateArray()
                .Select(ParseResetCredit)
                .OfType<RateLimitResetCredit>()
                .ToArray();
        }

        return new ResetCreditSummary(Math.Max(0, count), details);
    }

    private static RateLimitResetCredit? ParseResetCredit(JsonElement credit)
    {
        if (ReadString(credit, "id") is not { } id ||
            ReadString(credit, "status") is not { } status ||
            ReadUnixTime(credit, "grantedAt") is not { } grantedAt)
        {
            return null;
        }

        return new RateLimitResetCredit(
            id,
            status,
            grantedAt,
            ReadUnixTime(credit, "expiresAt"),
            ReadString(credit, "title"),
            ReadString(credit, "description"));
    }

    private static string BuildBucketLabel(string limitId, string? limitName)
    {
        if (!string.IsNullOrWhiteSpace(limitName))
        {
            return limitName;
        }

        return string.Equals(limitId, "codex", StringComparison.OrdinalIgnoreCase)
            ? "Codex"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(limitId.Replace('_', ' '));
    }

    private static string BuildWindowLabel(long? duration, string windowKey) => duration switch
    {
        >= 10_000 => "Weekly limit",
        >= 1_440 when duration % 1_440 == 0 => $"{duration / 1_440}d limit",
        >= 60 when duration % 60 == 0 => $"{duration / 60}h limit",
        > 0 => $"{duration}m limit",
        _ => windowKey == "primary" ? "Primary limit" : "Secondary limit"
    };

    private static DateTimeOffset? ReadUnixTime(JsonElement element, string propertyName)
    {
        if (ReadInt64(element, propertyName) is not { } unixSeconds)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static long? ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;

    private static bool? ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
