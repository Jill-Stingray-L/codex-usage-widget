using System.Drawing;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Tests;

public sealed class FullscreenWindowDetectorTests
{
    private static readonly Rectangle MonitorBounds = Rectangle.FromLTRB(0, 0, 1920, 1080);

    [Fact]
    public void ExactMonitorBoundsAreFullscreen()
    {
        var result = FullscreenWindowDetector.CoversMonitor(MonitorBounds, MonitorBounds);

        Assert.True(result);
    }

    [Fact]
    public void OnePixelRoundingDifferenceIsFullscreen()
    {
        var windowBounds = Rectangle.FromLTRB(1, 1, 1919, 1079);

        var result = FullscreenWindowDetector.CoversMonitor(windowBounds, MonitorBounds);

        Assert.True(result);
    }

    [Fact]
    public void MaximizedWindowAboveVisibleTaskbarIsNotFullscreen()
    {
        var windowBounds = Rectangle.FromLTRB(0, 0, 1920, 1040);

        var result = FullscreenWindowDetector.CoversMonitor(windowBounds, MonitorBounds);

        Assert.False(result);
    }

    [Fact]
    public void NonOverlappingBoundsAreNotFullscreen()
    {
        var windowBounds = Rectangle.FromLTRB(1920, 0, 3840, 1080);

        var result = FullscreenWindowDetector.CoversMonitor(windowBounds, MonitorBounds);

        Assert.False(result);
    }
}
