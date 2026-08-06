using System.Globalization;
using System.Text.Json;
using CodexUsageWidget.Domain;

namespace CodexUsageWidget.Infrastructure.Codex;

public static class CodexTokenUsageParser
{
    public static TokenActivitySummary? Parse(JsonElement result)
    {
        if (!result.TryGetProperty("summary", out var summary) ||
            summary.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dailyUsage = ParseDailyUsage(result);
        var parsed = new TokenActivitySummary(
            ReadInt64(summary, "lifetimeTokens"),
            ReadInt64(summary, "peakDailyTokens"),
            ReadInt64(summary, "longestRunningTurnSec"),
            ReadInt64(summary, "currentStreakDays"),
            ReadInt64(summary, "longestStreakDays"),
            dailyUsage);

        return parsed.LifetimeTokens is null &&
               parsed.PeakDailyTokens is null &&
               parsed.LongestRunningTurnSeconds is null &&
               parsed.CurrentStreakDays is null &&
               parsed.LongestStreakDays is null &&
               dailyUsage.Length == 0
            ? null
            : parsed;
    }

    private static DailyTokenUsage[] ParseDailyUsage(JsonElement result)
    {
        if (!result.TryGetProperty("dailyUsageBuckets", out var buckets) ||
            buckets.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DailyTokenUsage>();
        }

        var usage = new List<DailyTokenUsage>();
        foreach (var bucket in buckets.EnumerateArray())
        {
            if (ReadString(bucket, "startDate") is not { } dateText ||
                !DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date) ||
                ReadInt64(bucket, "tokens") is not { } tokens)
            {
                continue;
            }

            usage.Add(new DailyTokenUsage(date, Math.Max(0, tokens)));
        }

        return usage.OrderBy(item => item.Date).ToArray();
    }

    private static long? ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
