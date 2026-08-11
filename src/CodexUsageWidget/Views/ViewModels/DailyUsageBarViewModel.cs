namespace CodexUsageWidget.Views.ViewModels;

public sealed record DailyUsageBarViewModel(
    double Height,
    string DateText,
    string TokensText,
    string PeakComparisonText);
