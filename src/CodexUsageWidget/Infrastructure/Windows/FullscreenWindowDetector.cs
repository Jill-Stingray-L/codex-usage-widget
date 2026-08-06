using System.Drawing;
using System.Runtime.InteropServices;

namespace CodexUsageWidget.Infrastructure.Windows;

public static class FullscreenWindowDetector
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint DwmExtendedFrameBounds = 9;
    private const int EdgeTolerance = 1;

    public static bool IsForegroundWindowFullscreenOnMonitor(IntPtr referenceWindowHandle)
    {
        if (referenceWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero ||
            foregroundWindow == referenceWindowHandle ||
            foregroundWindow == GetShellWindow() ||
            !IsWindowVisible(foregroundWindow) ||
            IsIconic(foregroundWindow))
        {
            return false;
        }

        if (GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId) == 0 ||
            foregroundProcessId == (uint)Environment.ProcessId ||
            IsShellSurface(foregroundWindow))
        {
            return false;
        }

        var referenceMonitor = MonitorFromWindow(referenceWindowHandle, MonitorDefaultToNearest);
        var foregroundMonitor = MonitorFromWindow(foregroundWindow, MonitorDefaultToNearest);
        if (referenceMonitor == IntPtr.Zero || foregroundMonitor != referenceMonitor)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(referenceMonitor, ref monitorInfo) ||
            !TryGetWindowBounds(foregroundWindow, out var windowBounds))
        {
            return false;
        }

        return CoversMonitor(windowBounds.ToRectangle(), monitorInfo.Monitor.ToRectangle());
    }

    public static bool CoversMonitor(
        Rectangle windowBounds,
        Rectangle monitorBounds)
    {
        return windowBounds.Left <= monitorBounds.Left + EdgeTolerance &&
               windowBounds.Top <= monitorBounds.Top + EdgeTolerance &&
               windowBounds.Right >= monitorBounds.Right - EdgeTolerance &&
               windowBounds.Bottom >= monitorBounds.Bottom - EdgeTolerance;
    }

    private static bool TryGetWindowBounds(IntPtr windowHandle, out NativeRect bounds)
    {
        var result = DwmGetWindowAttribute(
            windowHandle,
            DwmExtendedFrameBounds,
            out bounds,
            Marshal.SizeOf<NativeRect>());

        return result == 0 || GetWindowRect(windowHandle, out bounds);
    }

    private static bool IsShellSurface(IntPtr windowHandle)
    {
        var classNameBuffer = new char[64];
        var classNameLength = GetClassName(windowHandle, classNameBuffer, classNameBuffer.Length);
        if (classNameLength == 0)
        {
            return false;
        }

        return new string(classNameBuffer, 0, classNameLength) is "Progman" or "WorkerW" or
            "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        [Out] char[] className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        uint attribute,
        out NativeRect attributeValue,
        int attributeSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
}
