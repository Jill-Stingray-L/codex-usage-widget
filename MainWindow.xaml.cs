using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
    private readonly TaskbarLabelWindow _taskbarLabel;
    private Forms.ToolStripMenuItem? _desktopWidgetModeItem;
    private Forms.ToolStripMenuItem? _taskbarIndicatorModeItem;
    private System.Drawing.Icon? _generatedTrayIcon;
    private WidgetDisplayMode _displayMode = LoadDisplayMode();
    private double _primaryUsedPercent;
    private bool _isRefreshing;
    private bool _allowClose;

    public bool StartsInTaskbarIndicatorMode => _displayMode == WidgetDisplayMode.TaskbarIndicator;

    public MainWindow()
    {
        InitializeComponent();

        PrimaryProgressTrack.SizeChanged += (_, _) => UpdatePrimaryProgressFill();

        _taskbarLabel = new TaskbarLabelWindow();
        _taskbarLabel.OpenRequested += (_, _) => Dispatcher.Invoke(ShowWidget);
        _taskbarLabel.RefreshRequested += (_, _) => Dispatcher.InvokeAsync(RefreshAsync);
        _taskbarLabel.DesktopModeRequested += (_, _) =>
            Dispatcher.Invoke(() => SetDisplayMode(WidgetDisplayMode.DesktopWidget));
        _taskbarLabel.ExitRequested += (_, _) => Dispatcher.Invoke(ExitApplication);

        _client.RateLimitsChanged += (_, _) => Dispatcher.InvokeAsync(RefreshAsync);
        _client.DiagnosticMessage += (_, message) => System.Diagnostics.Debug.WriteLine(message);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _trayIcon = BuildTrayIcon();

        Loaded += MainWindow_OnLoaded;
        Deactivated += MainWindow_OnDeactivated;
        Closing += MainWindow_OnClosing;
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(ShowWidget));
        menu.Items.Add("Refresh", null, (_, _) => Dispatcher.InvokeAsync(RefreshAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());

        var displayModeMenu = new Forms.ToolStripMenuItem("Display mode");
        _desktopWidgetModeItem = new Forms.ToolStripMenuItem(
            "Desktop widget",
            null,
            (_, _) => Dispatcher.Invoke(() => SetDisplayMode(WidgetDisplayMode.DesktopWidget)));
        _taskbarIndicatorModeItem = new Forms.ToolStripMenuItem(
            "Taskbar label",
            null,
            (_, _) => Dispatcher.Invoke(() => SetDisplayMode(WidgetDisplayMode.TaskbarIndicator)));
        displayModeMenu.DropDownItems.Add(_desktopWidgetModeItem);
        displayModeMenu.DropDownItems.Add(_taskbarIndicatorModeItem);
        menu.Items.Add(displayModeMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _generatedTrayIcon = CreateUsageIcon(null);
        var icon = new Forms.NotifyIcon
        {
            Icon = _generatedTrayIcon,
            Text = "Codex Usage Widget",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(ShowWidget);
            }
        };
        UpdateDisplayModeMenu();
        return icon;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearWorkAreaEdge();
        if (_displayMode == WidgetDisplayMode.TaskbarIndicator)
        {
            _taskbarLabel.ShowLabel();
        }
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
        RenderWindow(primary);
        PrimaryPanel.Visibility = Visibility.Visible;

        RemainingText.Text = $"{Math.Round(primary.RemainingPercent):0}%";
        SetStatus(snapshot.PlanType is null ? "Live · ChatGPT" : $"Live · {snapshot.PlanType}", "#65D892");
        UpdatedText.Text = $"Local-only · updated {snapshot.FetchedAt:HH:mm:ss}";
        _trayIcon.Text = $"Codex · {Math.Round(primary.RemainingPercent):0}% remaining";
        UpdateTrayUsageIcon(primary.RemainingPercent);
        _taskbarLabel.UpdateUsage(primary.RemainingPercent, primary.ResetsAt);
    }

    private void RenderWindow(UsageWindow window)
    {
        PrimaryLabel.Text = window.Label;
        PrimaryPercent.Text = $"{Math.Round(window.UsedPercent):0}% used";
        _primaryUsedPercent = Math.Clamp(window.UsedPercent, 0d, 100d);
        PrimaryProgressFill.Background = new SolidColorBrush(ColorForRemaining(window.RemainingPercent));
        UpdatePrimaryProgressFill();
        PrimaryReset.Text = window.ResetsAt is null
            ? "Reset time unavailable"
            : $"Resets {FormatReset(window.ResetsAt.Value)}";
    }

    private void UpdatePrimaryProgressFill()
    {
        PrimaryProgressFill.Width = Math.Max(0d, PrimaryProgressTrack.ActualWidth) *
                                    (_primaryUsedPercent / 100d);
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
        _trayIcon.Text = "Codex Usage · unavailable";
        UpdateTrayUsageIcon(null);
        _taskbarLabel.UpdateUsage(null, null);
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

    private void HideButton_OnClick(object sender, RoutedEventArgs e) =>
        SetDisplayMode(WidgetDisplayMode.TaskbarIndicator);

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => ExitApplication();

    private void ShowWidget()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void SetDisplayMode(WidgetDisplayMode mode)
    {
        _displayMode = mode;
        SaveDisplayMode(mode);
        UpdateDisplayModeMenu();

        if (mode == WidgetDisplayMode.DesktopWidget)
        {
            _taskbarLabel.HideLabel();
            ShowWidget();
        }
        else
        {
            _taskbarLabel.ShowLabel();
            Hide();
        }
    }

    private void UpdateDisplayModeMenu()
    {
        if (_desktopWidgetModeItem is not null)
        {
            _desktopWidgetModeItem.Checked = _displayMode == WidgetDisplayMode.DesktopWidget;
        }

        if (_taskbarIndicatorModeItem is not null)
        {
            _taskbarIndicatorModeItem.Checked = _displayMode == WidgetDisplayMode.TaskbarIndicator;
        }
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        if (_displayMode == WidgetDisplayMode.TaskbarIndicator)
        {
            Hide();
        }
    }

    private void UpdateTrayUsageIcon(double? remainingPercent)
    {
        var nextIcon = CreateUsageIcon(remainingPercent);
        var previousIcon = _generatedTrayIcon;
        _generatedTrayIcon = nextIcon;
        _trayIcon.Icon = nextIcon;
        previousIcon?.Dispose();
    }

    private static System.Drawing.Icon CreateUsageIcon(double? remainingPercent)
    {
        using var bitmap = new System.Drawing.Bitmap(64, 64);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var indicatorColor = remainingPercent switch
        {
            <= 10 => System.Drawing.Color.FromArgb(240, 112, 112),
            <= 25 => System.Drawing.Color.FromArgb(240, 179, 94),
            _ => System.Drawing.Color.FromArgb(101, 216, 146)
        };

        using var backgroundBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(22, 29, 39));
        using var borderPen = new System.Drawing.Pen(indicatorColor, 6f);
        graphics.FillEllipse(backgroundBrush, 3, 3, 58, 58);
        graphics.DrawEllipse(borderPen, 6, 6, 52, 52);

        var text = remainingPercent is null
            ? "?"
            : Math.Round(Math.Clamp(remainingPercent.Value, 0d, 100d)).ToString("0");
        var fontSize = text.Length >= 3 ? 18f : 24f;
        using var font = new System.Drawing.Font(
            "Segoe UI",
            fontSize,
            System.Drawing.FontStyle.Bold,
            System.Drawing.GraphicsUnit.Pixel);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        var textSize = graphics.MeasureString(text, font);
        graphics.DrawString(
            text,
            font,
            textBrush,
            (64f - textSize.Width) / 2f,
            (64f - textSize.Height) / 2f - 1f);

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = System.Drawing.Icon.FromHandle(iconHandle);
            return (System.Drawing.Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static WidgetDisplayMode LoadDisplayMode()
    {
        try
        {
            var value = File.ReadAllText(DisplayModePath).Trim();
            return value.Equals("taskbar", StringComparison.OrdinalIgnoreCase)
                ? WidgetDisplayMode.TaskbarIndicator
                : WidgetDisplayMode.DesktopWidget;
        }
        catch
        {
            return WidgetDisplayMode.DesktopWidget;
        }
    }

    private static void SaveDisplayMode(WidgetDisplayMode mode)
    {
        try
        {
            var directory = Path.GetDirectoryName(DisplayModePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                DisplayModePath,
                mode == WidgetDisplayMode.TaskbarIndicator ? "taskbar" : "widget");
        }
        catch
        {
            // Preferences are best-effort; usage display should keep working.
        }
    }

    private static string DisplayModePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexUsageWidget",
        "display-mode.txt");

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

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
            SetDisplayMode(WidgetDisplayMode.TaskbarIndicator);
            return;
        }

        _refreshTimer.Stop();
        _taskbarLabel.HideLabel();
        _taskbarLabel.Close();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _generatedTrayIcon?.Dispose();
        _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        System.Windows.Application.Current.Shutdown();
    }

    private enum WidgetDisplayMode
    {
        DesktopWidget,
        TaskbarIndicator
    }
}
