using System.Windows;
using System.Windows.Media.Animation;

namespace CodexUsageWidget.Views.Controls;

public partial class ActivityDotsIndicator : System.Windows.Controls.UserControl
{
    private static readonly System.Windows.Media.Brush DefaultDotBrush = CreateDefaultDotBrush();
    private static readonly TimeSpan MinimumVisibleDuration = TimeSpan.FromMilliseconds(700d);
    // Matches the 100 ms settle plus 300 ms collapse sequence in XAML.
    private static readonly TimeSpan CompletionSequenceDuration = TimeSpan.FromMilliseconds(400d);

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(ActivityDotsIndicator),
        new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty ShowIdleDotProperty = DependencyProperty.Register(
        nameof(ShowIdleDot),
        typeof(bool),
        typeof(ActivityDotsIndicator),
        new PropertyMetadata(false, OnShowIdleDotChanged));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush),
        typeof(System.Windows.Media.Brush),
        typeof(ActivityDotsIndicator),
        new PropertyMetadata(DefaultDotBrush));

    public static readonly DependencyProperty DotVerticalOffsetProperty = DependencyProperty.Register(
        nameof(DotVerticalOffset),
        typeof(double),
        typeof(ActivityDotsIndicator),
        new PropertyMetadata(0d));

    private readonly Storyboard _expandStoryboard;
    private readonly Storyboard _waveStoryboard;
    private readonly Storyboard _settleStoryboard;
    private readonly Storyboard _collapseStoryboard;
    private readonly System.Windows.Threading.DispatcherTimer _completionDelayTimer;
    private bool _isReady;
    private ActivityVisualState _visualState;
    private long _activationStartedTimestamp;

    public ActivityDotsIndicator()
    {
        InitializeComponent();

        _expandStoryboard = FindStoryboard("ActivityExpandStoryboard");
        _waveStoryboard = FindStoryboard("ActivityWaveStoryboard");
        _settleStoryboard = FindStoryboard("ActivitySettleStoryboard");
        _collapseStoryboard = FindStoryboard("ActivityCollapseStoryboard");
        _expandStoryboard.Completed += ExpandStoryboardOnCompleted;
        _settleStoryboard.Completed += SettleStoryboardOnCompleted;
        _collapseStoryboard.Completed += CollapseStoryboardOnCompleted;

        _completionDelayTimer = new System.Windows.Threading.DispatcherTimer();
        _completionDelayTimer.Tick += CompletionDelayTimerOnTick;

        Loaded += ActivityDotsIndicatorOnLoaded;
        Unloaded += ActivityDotsIndicatorOnUnloaded;
        IsVisibleChanged += ActivityDotsIndicatorOnIsVisibleChanged;
        ResetToIdleState();
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool ShowIdleDot
    {
        get => (bool)GetValue(ShowIdleDotProperty);
        set => SetValue(ShowIdleDotProperty, value);
    }

    public System.Windows.Media.Brush DotBrush
    {
        get => (System.Windows.Media.Brush)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public double DotVerticalOffset
    {
        get => (double)GetValue(DotVerticalOffsetProperty);
        set => SetValue(DotVerticalOffsetProperty, value);
    }

    private static void OnIsActiveChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var indicator = (ActivityDotsIndicator)dependencyObject;
        if (indicator._isReady)
        {
            indicator.ApplyVisualState();
        }
        else
        {
            indicator.Visibility = indicator.IsActive || indicator.ShowIdleDot
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static void OnShowIdleDotChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var indicator = (ActivityDotsIndicator)dependencyObject;
        if (indicator._visualState == ActivityVisualState.Idle)
        {
            indicator.Visibility = indicator.ShowIdleDot
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void ActivityDotsIndicatorOnLoaded(object sender, RoutedEventArgs e)
    {
        _isReady = true;
        ResetToIdleState();
        if (IsActive)
        {
            BeginOrResumeActivity();
        }
    }

    private void ActivityDotsIndicatorOnUnloaded(object sender, RoutedEventArgs e)
    {
        _isReady = false;
        ResetToIdleState();
    }

    private void ActivityDotsIndicatorOnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            StartWaveIfReady();
        }
        else
        {
            StopWave();
        }
    }

    private void ApplyVisualState()
    {
        if (IsActive)
        {
            BeginOrResumeActivity();
            return;
        }

        ScheduleCompletion();
    }

    private void BeginOrResumeActivity()
    {
        CancelCompletionDelay();
        Visibility = Visibility.Visible;

        if (_visualState is ActivityVisualState.Expanding or ActivityVisualState.Active)
        {
            StartWaveIfReady();
            return;
        }

        CaptureCurrentVisualStateAndStopAnimations();
        _activationStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _visualState = ActivityVisualState.Expanding;
        _expandStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void ScheduleCompletion()
    {
        if (_visualState is ActivityVisualState.Idle or
            ActivityVisualState.Settling or
            ActivityVisualState.Collapsing)
        {
            return;
        }

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_activationStartedTimestamp);
        var delay = MinimumVisibleDuration - CompletionSequenceDuration - elapsed;
        if (delay <= TimeSpan.Zero)
        {
            BeginSettle();
            return;
        }

        _completionDelayTimer.Stop();
        _completionDelayTimer.Interval = delay;
        _completionDelayTimer.Start();
    }

    private void CompletionDelayTimerOnTick(object? sender, EventArgs e)
    {
        CancelCompletionDelay();
        if (!IsActive)
        {
            BeginSettle();
        }
    }

    private void BeginSettle()
    {
        if (_visualState is ActivityVisualState.Idle or
            ActivityVisualState.Settling or
            ActivityVisualState.Collapsing)
        {
            return;
        }

        CancelCompletionDelay();
        CaptureCurrentVisualStateAndStopAnimations();
        _visualState = ActivityVisualState.Settling;
        _settleStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void ExpandStoryboardOnCompleted(object? sender, EventArgs e)
    {
        if (_visualState != ActivityVisualState.Expanding)
        {
            return;
        }

        _visualState = ActivityVisualState.Active;
        StartWaveIfReady();
    }

    private void SettleStoryboardOnCompleted(object? sender, EventArgs e)
    {
        if (_visualState != ActivityVisualState.Settling)
        {
            return;
        }

        CaptureCurrentVisualStateAndStopAnimations();
        _visualState = ActivityVisualState.Collapsing;
        _collapseStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void CollapseStoryboardOnCompleted(object? sender, EventArgs e)
    {
        if (_visualState != ActivityVisualState.Collapsing)
        {
            return;
        }

        if (IsActive)
        {
            BeginOrResumeActivity();
            return;
        }

        ResetToIdleState();
    }

    private void StartWaveIfReady()
    {
        if (_isReady && _visualState == ActivityVisualState.Active && IsVisible)
        {
            _waveStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        }
    }

    private void StopWave() => _waveStoryboard.Remove(this);

    private void StopAllStoryboards()
    {
        _expandStoryboard.Remove(this);
        _waveStoryboard.Remove(this);
        _settleStoryboard.Remove(this);
        _collapseStoryboard.Remove(this);
    }

    private void CancelCompletionDelay() => _completionDelayTimer.Stop();

    private void CaptureCurrentVisualStateAndStopAnimations()
    {
        var leftOpacity = LeftActivityDot.Opacity;
        var leftOffset = LeftActivityDotTransform.X;
        var centerOpacity = CenterActivityDot.Opacity;
        var centerOffset = CenterActivityDotTransform.X;
        var rightOpacity = RightActivityDot.Opacity;
        var rightScaleX = RightActivityDotScale.ScaleX;
        var rightScaleY = RightActivityDotScale.ScaleY;

        StopAllStoryboards();

        LeftActivityDot.Opacity = leftOpacity;
        LeftActivityDotTransform.X = leftOffset;
        CenterActivityDot.Opacity = centerOpacity;
        CenterActivityDotTransform.X = centerOffset;
        RightActivityDot.Opacity = rightOpacity;
        RightActivityDotScale.ScaleX = rightScaleX;
        RightActivityDotScale.ScaleY = rightScaleY;
    }

    private void ResetToIdleState()
    {
        CancelCompletionDelay();
        StopAllStoryboards();
        _visualState = ActivityVisualState.Idle;
        LeftActivityDot.Opacity = 0d;
        LeftActivityDotTransform.X = 0d;
        CenterActivityDot.Opacity = 0d;
        CenterActivityDotTransform.X = 0d;
        RightActivityDot.Opacity = 1d;
        RightActivityDotScale.ScaleX = 1d;
        RightActivityDotScale.ScaleY = 1d;
        Visibility = ShowIdleDot ? Visibility.Visible : Visibility.Collapsed;
    }

    private Storyboard FindStoryboard(string resourceName) =>
        ((Storyboard)FindResource(resourceName)).Clone();

    private static System.Windows.Media.SolidColorBrush CreateDefaultDotBrush()
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(216, 216, 216));
        brush.Freeze();
        return brush;
    }

    private enum ActivityVisualState
    {
        Idle,
        Expanding,
        Active,
        Settling,
        Collapsing
    }
}
