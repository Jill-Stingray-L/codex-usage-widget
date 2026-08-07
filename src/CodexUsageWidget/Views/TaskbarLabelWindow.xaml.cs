using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Views;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the window lifecycle; the Closed handler releases the native hooks.")]
public partial class TaskbarLabelWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer _positionTimer;
    private readonly WindowChangeWatcher _windowChangeWatcher;
    private readonly Storyboard _activityExpandStoryboard;
    private readonly Storyboard _activityWaveStoryboard;
    private readonly Storyboard _activityCollapseStoryboard;
    private IntPtr _windowHandle;
    private bool _labelRequested;
    private bool _realActivityIsActive;
    private bool _previewActivityIsActive;
    private bool _isTaskActive;
    private int _visibilityUpdateQueued;

    public TaskbarLabelWindow()
    {
        InitializeComponent();

        _activityExpandStoryboard = FindStoryboard("ActivityExpandStoryboard");
        _activityWaveStoryboard = FindStoryboard("ActivityWaveStoryboard");
        _activityCollapseStoryboard = FindStoryboard("ActivityCollapseStoryboard");
        _activityExpandStoryboard.Completed += (_, _) => StartActivityWaveIfVisible();
        _activityCollapseStoryboard.Completed += (_, _) => ResetActivityAnimation();

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
        _positionTimer.Tick += (_, _) => UpdateVisibilityAndPosition();

        _windowChangeWatcher = new WindowChangeWatcher(QueueVisibilityUpdate);
        Closed += (_, _) => _windowChangeWatcher.Dispose();
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? ActivityDotsSetupRequested;

    public event EventHandler? DesktopModeRequested;

    public event EventHandler? ExitRequested;

    public bool IsPointerOver => IsMouseOver;

    public void ShowLabel()
    {
        _labelRequested = true;
        new WindowInteropHelper(this).EnsureHandle();
        _positionTimer.Start();
        UpdateVisibilityAndPosition();
    }

    public void HideLabel()
    {
        _labelRequested = false;
        _positionTimer.Stop();
        StopActivityWave();
        Hide();
    }

    public void SetActivityState(bool isActive)
    {
        _realActivityIsActive = isActive;
        ApplyEffectiveActivityState();
    }

    private void ApplyEffectiveActivityState()
    {
        var isActive = _realActivityIsActive || _previewActivityIsActive;
        if (_isTaskActive == isActive)
        {
            return;
        }

        _isTaskActive = isActive;

        if (isActive)
        {
            SetActivityLayout(isExpanded: true);
            _activityCollapseStoryboard.Remove(this);
            _activityExpandStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
            return;
        }

        StopActivityWave();
        _activityCollapseStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
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
        if (_windowHandle != IntPtr.Zero)
        {
            TaskbarWindowInterop.PositionNextToNotificationArea(_windowHandle, Width, Height);
        }
    }

    private void UpdateVisibilityAndPosition()
    {
        if (!_labelRequested || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (FullscreenWindowDetector.IsForegroundWindowFullscreenOnMonitor(_windowHandle))
        {
            if (IsVisible)
            {
                StopActivityWave();
                Hide();
            }

            return;
        }

        if (!IsVisible)
        {
            Reposition();
            Show();
            StartActivityWaveIfVisible();
            return;
        }

        Reposition();
    }

    private void QueueVisibilityUpdate()
    {
        if (Interlocked.Exchange(ref _visibilityUpdateQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                Interlocked.Exchange(ref _visibilityUpdateQueued, 0);
                UpdateVisibilityAndPosition();
            },
            System.Windows.Threading.DispatcherPriority.Send);
    }

    private Storyboard FindStoryboard(string resourceName) =>
        ((Storyboard)FindResource(resourceName)).Clone();

    private void StartActivityWaveIfVisible()
    {
        if (_isTaskActive && IsVisible)
        {
            _activityWaveStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        }
    }

    private void StopActivityWave() => _activityWaveStoryboard.Remove(this);

    private void ResetActivityAnimation()
    {
        if (_isTaskActive)
        {
            return;
        }

        _activityExpandStoryboard.Remove(this);
        _activityCollapseStoryboard.Remove(this);
        SetActivityLayout(isExpanded: false);
    }

    private void SetActivityLayout(bool isExpanded)
    {
        Width = isExpanded ? 102d : 94d;
        LabelSurface.Padding = isExpanded
            ? new Thickness(8d, 0d, 0d, 0d)
            : new Thickness(0d);
        Reposition();
    }

    private void LabelSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        ToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OpenMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void ActivityDotsMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ActivityDotsSetupRequested?.Invoke(this, EventArgs.Empty);

    private void ActivityPreviewMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _previewActivityIsActive = ActivityPreviewMenuItem.IsChecked;
        ApplyEffectiveActivityState();
    }

    private void DesktopModeMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        DesktopModeRequested?.Invoke(this, EventArgs.Empty);

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}
