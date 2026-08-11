using System.Globalization;
using CodexUsageWidget.Domain;
using CodexUsageWidget.Views.ViewModels;

namespace CodexUsageWidget.Tests;

public sealed class TokenActivityViewModelTests
{
    [Fact]
    public void DailyBarsExposeStyledTooltipContentRelativeToVisiblePeak()
    {
        var date = new DateOnly(2026, 8, 11);
        var activity = new TokenActivitySummary(
            LifetimeTokens: null,
            PeakDailyTokens: null,
            LongestRunningTurnSeconds: null,
            CurrentStreakDays: null,
            LongestStreakDays: null,
            DailyUsage:
            [
                new DailyTokenUsage(date, 50_000),
                new DailyTokenUsage(date.AddDays(1), 100_000)
            ]);

        var viewModel = new TokenActivityViewModel(activity);

        var bar = viewModel.DailyBars[0];
        Assert.Equal(date.ToString("dddd, MMMM d", CultureInfo.CurrentCulture), bar.DateText);
        Assert.Equal($"{50_000.ToString("N0", CultureInfo.CurrentCulture)} tokens", bar.TokensText);
        Assert.Equal("50% of chart peak", bar.PeakComparisonText);
    }
}
