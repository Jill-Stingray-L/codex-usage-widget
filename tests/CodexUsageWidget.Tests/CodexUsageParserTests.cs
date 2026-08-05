using System.Text.Json;
using CodexUsageWidget.Infrastructure.Codex;

namespace CodexUsageWidget.Tests;

public sealed class CodexUsageParserTests
{
    [Fact]
    public void ParseReadsCodexWindowAndExcludesSparkBucket()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimitsByLimitId": {
                "codex": {
                  "limitId": "codex",
                  "planType": "pro",
                  "primary": {
                    "usedPercent": 25,
                    "windowDurationMins": 10080,
                    "resetsAt": 1893456000
                  }
                },
                "codex_spark": {
                  "limitId": "codex_spark",
                  "primary": {
                    "usedPercent": 90,
                    "windowDurationMins": 300
                  }
                }
              }
            }
            """);
        var fetchedAt = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var snapshot = CodexUsageParser.Parse(document.RootElement, fetchedAt);

        var window = Assert.Single(snapshot.Windows);
        Assert.Equal("codex", window.LimitId);
        Assert.Equal("Weekly window", window.Label);
        Assert.Equal(25, window.UsedPercent);
        Assert.Equal(75, window.RemainingPercent);
        Assert.Equal("pro", snapshot.PlanType);
        Assert.Equal(fetchedAt, snapshot.FetchedAt);
    }

    [Fact]
    public void ParseSupportsLegacyRateLimitsShapeAndClampsPercentages()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "limitId": "codex",
                "primary": {
                  "usedPercent": 125,
                  "windowDurationMins": 300
                }
              }
            }
            """);

        var snapshot = CodexUsageParser.Parse(document.RootElement);

        var window = Assert.Single(snapshot.Windows);
        Assert.Equal(100, window.UsedPercent);
        Assert.Equal(0, window.RemainingPercent);
        Assert.Equal("5h window", window.Label);
    }

    [Fact]
    public void ParseOrdersShorterWindowsBeforeLongerWindows()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "limitId": "codex",
                "primary": { "usedPercent": 10, "windowDurationMins": 10080 },
                "secondary": { "usedPercent": 20, "windowDurationMins": 300 }
              }
            }
            """);

        var snapshot = CodexUsageParser.Parse(document.RootElement);

        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(300, snapshot.Windows[0].WindowDurationMinutes);
        Assert.Equal(10080, snapshot.Windows[1].WindowDurationMinutes);
    }
}
