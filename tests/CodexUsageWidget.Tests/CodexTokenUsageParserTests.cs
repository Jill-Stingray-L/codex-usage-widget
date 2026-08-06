using System.Text.Json;
using CodexUsageWidget.Infrastructure.Codex;

namespace CodexUsageWidget.Tests;

public sealed class CodexTokenUsageParserTests
{
    [Fact]
    public void ParseReadsSummaryAndOrdersValidDailyBuckets()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "summary": {
                "lifetimeTokens": 1234567,
                "peakDailyTokens": 45678,
                "longestRunningTurnSec": 540,
                "currentStreakDays": 8,
                "longestStreakDays": 14
              },
              "dailyUsageBuckets": [
                { "startDate": "2026-06-19", "tokens": 200 },
                { "startDate": "invalid", "tokens": 999 },
                { "startDate": "2026-06-18", "tokens": 100 }
              ]
            }
            """);

        var result = Assert.IsType<CodexUsageWidget.Domain.TokenActivitySummary>(
            CodexTokenUsageParser.Parse(document.RootElement));

        Assert.Equal(1_234_567, result.LifetimeTokens);
        Assert.Equal(new DateOnly(2026, 6, 18), result.DailyUsage[0].Date);
        Assert.Equal(new DateOnly(2026, 6, 19), result.DailyUsage[1].Date);
    }

    [Fact]
    public void ParseReturnsNullWhenNoActivityMetricsAreAvailable()
    {
        using var document = JsonDocument.Parse(
            """{ "summary": {}, "dailyUsageBuckets": null }""");

        Assert.Null(CodexTokenUsageParser.Parse(document.RootElement));
    }
}
