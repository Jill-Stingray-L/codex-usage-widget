using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageWidget.Models;
using CodexUsageWidget.Services;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget;

public partial class MainWindow : Window
{
    private readonly CodexAppServerClient _client = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _isRefreshing;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        _client.RateLimitsChanged += (_, _) => Dispatcher.InvokeAsync(RefreshAsync);
        _client.DiagnosticMessage += (_, message) => System.Diagnostics.Debug.WriteLine(message);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _trayIcon = BuildTrayIcon();

        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(ShowWidget));
        menu.Items.Add("Refresh", null, (_, _) => Dispatcher.InvokeAsync(RefreshAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "Codex Usage Widget",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
        return icon;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearWorkAreaEdge();
        _refreshTimer.Start();
        await RefreshAsync();
    }

    private void PositionNearWorkAreaEdge()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 20;
        Top = workArea.Bottom - Height - 20;
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        SetStatus("Syncing…", "#E6A85A");

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var snapshot = await _client.ReadUsageAsync(timeout.Token);
            RenderSnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            RenderError("Codex did not respond in time.");
        }
        catch (Exception ex)
        {
            RenderError(ToFriendlyError(ex.Message));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RenderSnapshot(UsageSnapshot snapshot)
    {
        if (snapshot.Windows.Count == 0)
        {
            RenderError("No subscription limits returned. Run codex login first.");
            return;
        }

        var primary = snapshot.Windows[0];
        RenderWindow(primary, PrimaryLabel, PrimaryPercent, PrimaryProgress, PrimaryReset);
        PrimaryPanel.Visibility = Visibility.Visible;

        RemainingText.Text = $"{Math.Round(primary.RemainingPercent):0}%";
        UpdateUsageArc(primary.RemainingPercent);
        SetStatus(snapshot.PlanType is null ? "Live · ChatGPT" : $"Live · {snapshot.PlanType}", "#65D892");
        UpdatedText.Text = $"Local-only · updated {snapshot.FetchedAt:HH:mm:ss}";
        _trayIcon.Text = $"Codex · {Math.Round(primary.RemainingPercent):0}% remaining";
    }

    private static void RenderWindow(
        UsageWindow window,
        System.Windows.Controls.TextBlock label,
        System.Windows.Controls.TextBlock percent,
        System.Windows.Controls.ProgressBar progress,
        System.Windows.Controls.TextBlock reset)
    {
        label.Text = window.Label;
        percent.Text = $"{Math.Round(window.UsedPercent):0}% used";
        progress.Value = window.UsedPercent;
        progress.Foreground = new SolidColorBrush(ColorForRemaining(window.RemainingPercent));
        reset.Text = window.ResetsAt is null
            ? "Reset time unavailable"
            : $"Resets {FormatReset(window.ResetsAt.Value)}";
    }

    private void RenderError(string message)
    {
        RemainingText.Text = "--%";
        UsageArc.Data = null;
        PrimaryLabel.Text = "Usage unavailable";
        PrimaryPercent.Text = string.Empty;
        PrimaryProgress.Value = 0;
        PrimaryReset.Text = message;
        UpdatedText.Text = "Local-only · click ↻ to retry";
        SetStatus("Offline", "#F07B7B");
        _trayIcon.Text = "Codex Usage · unavailable";
    }

    private static string ToFriendlyError(string message)
    {
        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex CLI was not found on PATH.";
        }

        if (message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Run codex login, then refresh.";
        }

        return message.Length > 100 ? message[..100] + "…" : message;
    }

    private void SetStatus(string text, string color)
    {
        StatusText.Text = text;
        StatusDot.Fill = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void UpdateUsageArc(double remainingPercent)
    {
        var value = Math.Clamp(remainingPercent, 0d, 100d);
        var center = new System.Windows.Point(33, 33);
        const double radius = 29.5;

        UsageArc.Stroke = new SolidColorBrush(ColorForRemaining(value));

        if (value <= 0.01)
        {
            UsageArc.Data = null;
            return;
        }

        if (value >= 99.99)
        {
            UsageArc.Data = new EllipseGeometry(center, radius, radius);
            return;
        }

        var startAngle = -90d;
        var endAngle = startAngle + (value / 100d * 360d);
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, endAngle);

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new System.Windows.Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = value > 50d
        });

        UsageArc.Data = new PathGeometry(new[] { figure });
    }

    private static System.Windows.Point PointOnCircle(System.Windows.Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new System.Windows.Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private static System.Windows.Media.Color ColorForRemaining(double remainingPercent) => remainingPercent switch
    {
        <= 10 => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F07070"),
        <= 25 => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F0B35E"),
        _ => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#65D892")
    };

    private static string FormatReset(DateTimeOffset reset)
    {
        var remaining = reset - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        if (remaining < TimeSpan.FromHours(24))
        {
            return $"in {Math.Max(1, (int)Math.Ceiling(remaining.TotalHours))}h · {reset:HH:mm}";
        }

        return $"{reset:ddd HH:mm}";
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

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void HideButton_OnClick(object sender, RoutedEventArgs e) => Hide();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => ExitApplication();

    private void ShowWidget()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _refreshTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        System.Windows.Application.Current.Shutdown();
    }
}
