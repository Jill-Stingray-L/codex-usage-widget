using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Views;

public partial class TaskbarLabelWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer _positionTimer;
    private IntPtr _windowHandle;

    public TaskbarLabelWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            TaskbarWindowInterop.ConfigureAsTaskbarOverlay(_windowHandle);
            Reposition();
        };

        _positionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _positionTimer.Tick += (_, _) => Reposition();
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? DesktopModeRequested;

    public event EventHandler? ExitRequested;

    public bool IsPointerOver => IsMouseOver;

    public void ShowLabel()
    {
        if (!IsVisible)
        {
            Show();
        }

        _positionTimer.Start();
        Reposition();
    }

    public void HideLabel()
    {
        _positionTimer.Stop();
        Hide();
    }

    public void UpdateUsage(double? remainingPercent, DateTimeOffset? resetsAt)
    {
        if (remainingPercent is null)
        {
            UsageText.Text = "--%";
            LabelSurface.ToolTip = "Codex usage is currently unavailable.";
            return;
        }

        var value = Math.Round(Math.Clamp(remainingPercent.Value, 0d, 100d));
        UsageText.Text = $"{value:0}%";
        LabelSurface.ToolTip = resetsAt is null
            ? $"Codex: {value:0}% remaining"
            : $"Codex: {value:0}% remaining · resets {resetsAt.Value:ddd HH:mm}";
    }

    private void Reposition()
    {
        if (_windowHandle != IntPtr.Zero && IsVisible)
        {
            TaskbarWindowInterop.PositionNextToNotificationArea(_windowHandle, Width, Height);
        }
    }

    private void LabelSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        ToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OpenMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void DesktopModeMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        DesktopModeRequested?.Invoke(this, EventArgs.Empty);

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}
