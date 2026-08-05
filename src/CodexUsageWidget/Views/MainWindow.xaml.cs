using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageWidget.Application;
using CodexUsageWidget.Domain;
using CodexUsageWidget.Infrastructure.Settings;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Views;

public partial class MainWindow : Window
{
    private readonly UsageMonitor _usageMonitor;
    private readonly DisplayModeStore _displayModeStore;
    private readonly TrayIconService _trayIcon;
    private readonly TaskbarLabelWindow _taskbarLabel = new();
    private WidgetDisplayMode _displayMode;
    private double _primaryUsedPercent;
    private bool _allowClose;

    public MainWindow(
        UsageMonitor usageMonitor,
        DisplayModeStore displayModeStore,
        TrayIconService trayIcon)
    {
        _usageMonitor = usageMonitor;
        _displayModeStore = displayModeStore;
        _trayIcon = trayIcon;
        _displayMode = displayModeStore.Load();

        InitializeComponent();
        PrimaryProgressTrack.SizeChanged += (_, _) => UpdatePrimaryProgressFill();
        WireEvents();
    }

    public bool StartsInTaskbarIndicatorMode => _displayMode == WidgetDisplayMode.TaskbarIndicator;

    private void WireEvents()
    {
        Loaded += MainWindowOnLoaded;
        Deactivated += MainWindowOnDeactivated;
        Closing += MainWindowOnClosing;

        _usageMonitor.RefreshStarted += UsageMonitorOnRefreshStarted;
        _usageMonitor.SnapshotUpdated += UsageMonitorOnSnapshotUpdated;
        _usageMonitor.RefreshFailed += UsageMonitorOnRefreshFailed;

        _taskbarLabel.OpenRequested += (_, _) =>
            Dispatcher.BeginInvoke(ShowWidget, DispatcherPriority.ApplicationIdle);
        _taskbarLabel.RefreshRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = _usageMonitor.RefreshAsync());
        _taskbarLabel.DesktopModeRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => SetDisplayMode(WidgetDisplayMode.DesktopWidget));
        _taskbarLabel.ExitRequested += (_, _) => Dispatcher.BeginInvoke(ExitApplication);

        _trayIcon.OpenRequested += (_, _) => Dispatcher.BeginInvoke(ShowWidget);
        _trayIcon.RefreshRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => _ = _usageMonitor.RefreshAsync());
        _trayIcon.DesktopModeRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => SetDisplayMode(WidgetDisplayMode.DesktopWidget));
        _trayIcon.TaskbarModeRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => SetDisplayMode(WidgetDisplayMode.TaskbarIndicator));
        _trayIcon.ExitRequested += (_, _) => Dispatcher.BeginInvoke(ExitApplication);
    }

    private async void MainWindowOnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearWorkAreaEdge();
        _trayIcon.SetDisplayMode(_displayMode);
        if (_displayMode == WidgetDisplayMode.TaskbarIndicator)
        {
            _taskbarLabel.ShowLabel();
        }

        await _usageMonitor.StartAsync();
    }

    private void UsageMonitorOnRefreshStarted() =>
        Dispatcher.BeginInvoke(() => SetStatus("Syncing…", "#E6A85A"));

    private void UsageMonitorOnSnapshotUpdated(UsageSnapshot snapshot) =>
        Dispatcher.BeginInvoke(() => RenderSnapshot(snapshot));

    private void UsageMonitorOnRefreshFailed(string message) =>
        Dispatcher.BeginInvoke(() => RenderError(message));

    private void RenderSnapshot(UsageSnapshot snapshot)
    {
        if (snapshot.Primary is not { } primary)
        {
            RenderError("No subscription limits returned. Run codex login first.");
            return;
        }

        RenderUsageWindow(primary);
        PrimaryPanel.Visibility = Visibility.Visible;
        RemainingText.Text = $"{Math.Round(primary.RemainingPercent):0}%";
        SetStatus(snapshot.PlanType is null ? "Live · ChatGPT" : $"Live · {snapshot.PlanType}", "#65D892");
        UpdatedText.Text = $"Local-only · updated {snapshot.FetchedAt:HH:mm:ss}";
        _trayIcon.UpdateUsage(primary.RemainingPercent);
        _taskbarLabel.UpdateUsage(primary.RemainingPercent, primary.ResetsAt);
    }

    private void RenderUsageWindow(UsageWindow window)
    {
        PrimaryLabel.Text = window.Label;
        PrimaryPercent.Text = $"{Math.Round(window.UsedPercent):0}% used";
        _primaryUsedPercent = Math.Clamp(window.UsedPercent, 0d, 100d);
        PrimaryProgressFill.Background = BrushFromHex(
            UsageTextFormatter.ColorForRemaining(window.RemainingPercent));
        UpdatePrimaryProgressFill();
        PrimaryReset.Text = window.ResetsAt is null
            ? "Reset time unavailable"
            : $"Resets {UsageTextFormatter.FormatReset(window.ResetsAt.Value)}";
    }

    private void RenderError(string message)
    {
        RemainingText.Text = "--%";
        PrimaryLabel.Text = "Usage unavailable";
        PrimaryPercent.Text = string.Empty;
        _primaryUsedPercent = 0;
        UpdatePrimaryProgressFill();
        PrimaryReset.Text = message;
        UpdatedText.Text = "Local-only · click ↻ to retry";
        SetStatus("Offline", "#F07B7B");
        _trayIcon.UpdateUsage(null);
        _taskbarLabel.UpdateUsage(null, null);
    }

    private void SetStatus(string text, string color)
    {
        StatusText.Text = text;
        StatusDot.Fill = BrushFromHex(color);
    }

    private static SolidColorBrush BrushFromHex(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private void UpdatePrimaryProgressFill()
    {
        PrimaryProgressFill.Width = Math.Max(0d, PrimaryProgressTrack.ActualWidth) *
                                    (_primaryUsedPercent / 100d);
    }

    private void PositionNearWorkAreaEdge()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 20;
        Top = workArea.Bottom - Height - 20;
    }

    private void ShowWidget()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void SetDisplayMode(WidgetDisplayMode mode)
    {
        _displayMode = mode;
        _displayModeStore.Save(mode);
        _trayIcon.SetDisplayMode(mode);

        if (mode == WidgetDisplayMode.DesktopWidget)
        {
            _taskbarLabel.HideLabel();
            ShowWidget();
            return;
        }

        _taskbarLabel.ShowLabel();
        Hide();
    }

    private void Widget_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            e.OriginalSource is not DependencyObject source ||
            FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        DragMove();
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) =>
        await _usageMonitor.RefreshAsync();

    private void HideButton_OnClick(object sender, RoutedEventArgs e) =>
        SetDisplayMode(WidgetDisplayMode.TaskbarIndicator);

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => ExitApplication();

    private void MainWindowOnDeactivated(object? sender, EventArgs e)
    {
        if (_displayMode == WidgetDisplayMode.TaskbarIndicator)
        {
            Hide();
        }
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void MainWindowOnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            SetDisplayMode(WidgetDisplayMode.TaskbarIndicator);
            return;
        }

        _taskbarLabel.HideLabel();
        _taskbarLabel.Close();
        _trayIcon.Dispose();
        _usageMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        System.Windows.Application.Current.Shutdown();
    }
}
