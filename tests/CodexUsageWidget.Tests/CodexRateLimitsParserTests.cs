using System.Text.Json;
using CodexUsageWidget.Infrastructure.Codex;

namespace CodexUsageWidget.Tests;

public sealed class CodexRateLimitsParserTests
{
    [Fact]
    public void ParseSeparatesGeneralAndModelSpecificLimits()
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
                "codex_bengalfox": {
                  "limitId": "codex_bengalfox",
                  "limitName": "GPT-5.3-Codex-Spark",
                  "primary": {
                    "usedPercent": 90,
                    "windowDurationMins": 300
                  }
                }
              }
            }
            """);

        var result = CodexRateLimitsParser.Parse(document.RootElement);

        Assert.Equal("pro", result.PlanType);
        Assert.Collection(
            result.Limits,
            general =>
            {
                Assert.True(general.IsGeneral);
                Assert.Equal("Codex", general.Label);
                var window = Assert.Single(general.Windows);
                Assert.Equal("Weekly limit", window.Label);
                Assert.Equal(75, window.RemainingPercent);
            },
            modelSpecific =>
            {
                Assert.False(modelSpecific.IsGeneral);
                Assert.Equal("GPT-5.3-Codex-Spark", modelSpecific.Label);
                Assert.Equal(10, Assert.Single(modelSpecific.Windows).RemainingPercent);
            });
    }

    [Fact]
    public void ParseReadsCreditsSpendControlAndResetCredits()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimitsByLimitId": {
                "codex": {
                  "limitId": "codex",
                  "primary": { "usedPercent": 20, "windowDurationMins": 300 },
                  "credits": { "hasCredits": true, "unlimited": false, "balance": "12.5" },
                  "individualLimit": {
                    "used": "5",
                    "limit": "20",
                    "remainingPercent": 75,
                    "resetsAt": 1893456000
                  },
                  "spendControlReached": false
                }
              },
              "rateLimitResetCredits": {
                "availableCount": 2,
                "credits": [{
                  "id": "reset-1",
                  "status": "available",
                  "grantedAt": 1893450000,
                  "expiresAt": 1893456000,
                  "title": "Rate-limit reset"
                }]
              }
            }
            """);

        var result = CodexRateLimitsParser.Parse(document.RootElement);

        var limit = Assert.Single(result.Limits);
        Assert.Equal("12.5", Assert.IsType<CodexUsageWidget.Domain.CreditBalance>(limit.Credits).Balance);
        Assert.Equal(75, Assert.IsType<CodexUsageWidget.Domain.SpendLimit>(limit.IndividualLimit).RemainingPercent);
        Assert.False(limit.SpendControlReached);
        var resetSummary = Assert.IsType<CodexUsageWidget.Domain.ResetCreditSummary>(result.ResetCredits);
        Assert.Equal(2, resetSummary.AvailableCount);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CodexUsageWidget.Domain.RateLimitResetCredit>>(
            resetSummary.Credits));
    }

    [Fact]
    public void ParseSupportsLegacyShapeAndClampsPercentages()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "limitId": "codex",
                "primary": { "usedPercent": 125, "windowDurationMins": 300 },
                "secondary": { "usedPercent": -10, "windowDurationMins": 10080 }
              }
            }
            """);

        var result = CodexRateLimitsParser.Parse(document.RootElement);

        var windows = Assert.Single(result.Limits).Windows;
        Assert.Equal(2, windows.Count);
        Assert.Equal(100, windows[0].UsedPercent);
        Assert.Equal(0, windows[0].RemainingPercent);
        Assert.Equal(0, windows[1].UsedPercent);
        Assert.Equal(100, windows[1].RemainingPercent);
    }
}
