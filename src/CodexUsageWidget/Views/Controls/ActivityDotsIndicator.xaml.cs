using System.Windows;
using System.Windows.Media.Animation;

namespace CodexUsageWidget.Views.Controls;

public partial class ActivityDotsIndicator : System.Windows.Controls.UserControl
{
    private static readonly System.Windows.Media.Brush DefaultDotBrush = CreateDefaultDotBrush();

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
    private readonly Storyboard _collapseStoryboard;
    private bool _isReady;
    private bool _isExpanded;

    public ActivityDotsIndicator()
    {
        InitializeComponent();

        _expandStoryboard = FindStoryboard("ActivityExpandStoryboard");
        _waveStoryboard = FindStoryboard("ActivityWaveStoryboard");
        _collapseStoryboard = FindStoryboard("ActivityCollapseStoryboard");
        _expandStoryboard.Completed += ExpandStoryboardOnCompleted;
        _collapseStoryboard.Completed += CollapseStoryboardOnCompleted;

        Loaded += ActivityDotsIndicatorOnLoaded;
        Unloaded += ActivityDotsIndicatorOnUnloaded;
        IsVisibleChanged += ActivityDotsIndicatorOnIsVisibleChanged;
        ResetToIdleState();
    }

    public event EventHandler? CollapseCompleted;

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
        if (!indicator.IsActive)
        {
            indicator.Visibility = indicator.ShowIdleDot
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void ActivityDotsIndicatorOnLoaded(object sender, RoutedEventArgs e)
    {
        _isReady = true;
        if (IsActive)
        {
            ApplyVisualState();
        }
        else
        {
            ResetToIdleState();
        }
    }

    private void ActivityDotsIndicatorOnUnloaded(object sender, RoutedEventArgs e)
    {
        _isReady = false;
        StopAllStoryboards();
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
            Visibility = Visibility.Visible;
            _collapseStoryboard.Remove(this);
            _waveStoryboard.Remove(this);
            _expandStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
            return;
        }

        StopWave();
        _expandStoryboard.Remove(this);
        _collapseStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void ExpandStoryboardOnCompleted(object? sender, EventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        _isExpanded = true;
        StartWaveIfReady();
    }

    private void CollapseStoryboardOnCompleted(object? sender, EventArgs e)
    {
        if (IsActive)
        {
            return;
        }

        ResetToIdleState();
        CollapseCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void StartWaveIfReady()
    {
        if (_isReady && _isExpanded && IsActive && IsVisible)
        {
            _waveStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        }
    }

    private void StopWave() => _waveStoryboard.Remove(this);

    private void StopAllStoryboards()
    {
        _expandStoryboard.Remove(this);
        _waveStoryboard.Remove(this);
        _collapseStoryboard.Remove(this);
    }

    private void ResetToIdleState()
    {
        StopAllStoryboards();
        _isExpanded = false;
        LeftActivityDot.Opacity = 0d;
        LeftActivityDotTransform.X = 0d;
        CenterActivityDot.Opacity = 0d;
        CenterActivityDotTransform.X = 0d;
        RightActivityDot.Opacity = 1d;
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
}
