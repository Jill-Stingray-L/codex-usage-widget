using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Domain;

namespace CodexUsageWidget.Infrastructure.Codex;

public static class CodexUsageParser
{
    public static UsageSnapshot Parse(JsonElement result, DateTimeOffset? fetchedAt = null)
    {
        var windows = new List<UsageWindow>();
        string? planType = null;

        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in buckets.EnumerateObject())
            {
                ParseBucket(property.Value, property.Name, windows, ref planType);
            }
        }

        if (windows.Count == 0 &&
            result.TryGetProperty("rateLimits", out var legacyBucket) &&
            legacyBucket.ValueKind == JsonValueKind.Object)
        {
            ParseBucket(legacyBucket, "codex", windows, ref planType);
        }

        var ordered = windows
            .OrderBy(window => window.WindowDurationMinutes ?? int.MaxValue)
            .ThenBy(window => window.LimitId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new UsageSnapshot(ordered, planType, fetchedAt ?? DateTimeOffset.Now);
    }

    private static void ParseBucket(
        JsonElement bucket,
        string fallbackId,
        ICollection<UsageWindow> destination,
        ref string? planType)
    {
        var limitId = ReadString(bucket, "limitId") ?? fallbackId;
        var limitName = ReadString(bucket, "limitName");
        planType ??= ReadString(bucket, "planType");

        if (IsExcludedLimit(limitId, limitName))
        {
            return;
        }

        AddWindow(bucket, "primary", limitId, limitName, destination);
        AddWindow(bucket, "secondary", limitId, limitName, destination);
    }

    private static bool IsExcludedLimit(string limitId, string? limitName) =>
        limitId.Contains("spark", StringComparison.OrdinalIgnoreCase) ||
        (limitName?.Contains("spark", StringComparison.OrdinalIgnoreCase) ?? false);

    private static void AddWindow(
        JsonElement bucket,
        string windowKey,
        string limitId,
        string? limitName,
        ICollection<UsageWindow> destination)
    {
        if (!bucket.TryGetProperty(windowKey, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usedElement) ||
            !usedElement.TryGetDouble(out var used))
        {
            return;
        }

        var duration = ReadInt32(window, "windowDurationMins");
        var resetsAt = ReadResetTime(window);
        var label = BuildWindowLabel(limitName, limitId, duration, windowKey);
        destination.Add(new UsageWindow(
            limitId,
            label,
            Math.Clamp(used, 0d, 100d),
            duration,
            resetsAt));
    }

    private static int? ReadInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static DateTimeOffset? ReadResetTime(JsonElement window)
    {
        if (!window.TryGetProperty("resetsAt", out var resetElement) ||
            !resetElement.TryGetInt64(out var unixSeconds))
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

    private static string BuildWindowLabel(
        string? limitName,
        string limitId,
        int? duration,
        string windowKey)
    {
        var durationLabel = duration switch
        {
            >= 10_000 => "Weekly window",
            >= 1_440 when duration % 1_440 == 0 => $"{duration / 1_440}d window",
            >= 60 when duration % 60 == 0 => $"{duration / 60}h window",
            > 0 => $"{duration}m window",
            _ => windowKey == "primary" ? "Primary window" : "Secondary window"
        };

        var bucketLabel = string.IsNullOrWhiteSpace(limitName) ||
                          string.Equals(limitName, "codex", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(limitId, "codex", StringComparison.OrdinalIgnoreCase)
            ? null
            : limitName.Replace('_', ' ');

        return bucketLabel is null
            ? durationLabel
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(bucketLabel) + " · " + durationLabel;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
