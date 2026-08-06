using CodexUsageWidget.Domain;
using CodexUsageWidget.Views.ViewModels;

namespace CodexUsageWidget.Tests;

public sealed class UsageWidgetViewModelTests
{
    [Fact]
    public void FromSnapshotUsesMostConstrainedGeneralWindowForHeadline()
    {
        var reset = DateTimeOffset.Now.AddHours(2);
        var snapshot = CreateSnapshot(
            new UsageLimitBucket(
                "codex",
                "Codex",
                IsGeneral: true,
                [
                    new UsageWindow("5h limit", 20, 300, reset),
                    new UsageWindow("Weekly limit", 85, 10_080, reset.AddDays(2))
                ],
                Credits: null,
                IndividualLimit: null,
                ReachedState: null,
                SpendControlReached: null));

        var viewModel = UsageWidgetViewModel.FromSnapshot(snapshot);

        Assert.Equal("15%", viewModel.HeadlineRemainingText);
        Assert.Equal("Weekly limit remaining", viewModel.HeadlineLabel);
        Assert.Equal(15, viewModel.HeadlineRemainingPercent);
        Assert.Equal(2, viewModel.GeneralLimits.Count);
    }

    [Fact]
    public void FromSnapshotKeepsModelSpecificLimitsOutOfGeneralList()
    {
        var snapshot = CreateSnapshot(
            new UsageLimitBucket(
                "codex",
                "Codex",
                IsGeneral: true,
                [new UsageWindow("Weekly limit", 25, 10_080, null)],
                Credits: null,
                IndividualLimit: null,
                ReachedState: null,
                SpendControlReached: null),
            new UsageLimitBucket(
                "codex_bengalfox",
                "GPT-5.3-Codex-Spark",
                IsGeneral: false,
                [new UsageWindow("Weekly limit", 90, 10_080, null)],
                Credits: null,
                IndividualLimit: null,
                ReachedState: null,
                SpendControlReached: null));

        var viewModel = UsageWidgetViewModel.FromSnapshot(snapshot);

        Assert.Single(viewModel.GeneralLimits);
        var modelLimit = Assert.Single(viewModel.ModelLimits);
        Assert.Contains("GPT-5.3-Codex-Spark", modelLimit.Label, StringComparison.Ordinal);
    }

    private static UsageSnapshot CreateSnapshot(params UsageLimitBucket[] limits) => new(
        new UsageRateLimits(limits, "pro", ResetCredits: null),
        TokenActivity: null,
        DateTimeOffset.Now);
}
