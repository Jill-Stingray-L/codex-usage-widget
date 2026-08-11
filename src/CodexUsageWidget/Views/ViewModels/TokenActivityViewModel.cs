using System.Globalization;
using CodexUsageWidget.Domain;

namespace CodexUsageWidget.Views.ViewModels;

public sealed class TokenActivityViewModel
{
    private const int MaximumChartDays = 28;

    public TokenActivityViewModel(TokenActivitySummary activity)
    {
        Metrics = BuildMetrics(activity);
        DailyBars = BuildDailyBars(activity.DailyUsage);
    }

    public IReadOnlyList<DetailMetricViewModel> Metrics { get; }

    public IReadOnlyList<DailyUsageBarViewModel> DailyBars { get; }

    public bool HasDailyUsage => DailyBars.Count > 0;

    private static List<DetailMetricViewModel> BuildMetrics(TokenActivitySummary activity)
    {
        var metrics = new List<DetailMetricViewModel>();
        AddMetric(metrics, "Lifetime tokens", FormatNumber(activity.LifetimeTokens));
        AddMetric(metrics, "Peak daily tokens", FormatNumber(activity.PeakDailyTokens));
        AddMetric(
            metrics,
            "Longest turn",
            activity.LongestRunningTurnSeconds is { } seconds
                ? FormatDuration(seconds)
                : null);
        AddMetric(
            metrics,
            "Current streak",
            activity.CurrentStreakDays is { } currentStreak
                ? $"{currentStreak:N0} days"
                : null);
        AddMetric(
            metrics,
            "Longest streak",
            activity.LongestStreakDays is { } longestStreak
                ? $"{longestStreak:N0} days"
                : null);
        return metrics;
    }

    private static DailyUsageBarViewModel[] BuildDailyBars(
        IReadOnlyList<DailyTokenUsage> dailyUsage)
    {
        var recent = dailyUsage.TakeLast(MaximumChartDays).ToArray();
        if (recent.Length == 0)
        {
            return Array.Empty<DailyUsageBarViewModel>();
        }

        var maximum = Math.Max(1L, recent.Max(item => item.Tokens));
        return recent
            .Select(item => new DailyUsageBarViewModel(
                Math.Max(3d, 44d * item.Tokens / maximum),
                item.Date.ToString("dddd, MMMM d", CultureInfo.CurrentCulture),
                $"{item.Tokens.ToString("N0", CultureInfo.CurrentCulture)} tokens",
                $"{100d * item.Tokens / maximum:0}% of chart peak"))
            .ToArray();
    }

    private static void AddMetric(
        List<DetailMetricViewModel> metrics,
        string label,
        string? value)
    {
        if (value is not null)
        {
            metrics.Add(new DetailMetricViewModel(label, value));
        }
    }

    private static string? FormatNumber(long? value) => value is null
        ? null
        : value.Value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatDuration(long seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes))}m";
    }
}
