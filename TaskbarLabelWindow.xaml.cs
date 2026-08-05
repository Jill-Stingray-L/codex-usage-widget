using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CodexUsageWidget;

public partial class TaskbarLabelWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly System.Windows.Threading.DispatcherTimer _positionTimer;
    private IntPtr _windowHandle;

    public event EventHandler? OpenRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? DesktopModeRequested;
    public event EventHandler? ExitRequested;

    public TaskbarLabelWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
            SetWindowLongPtr(
                _windowHandle,
                GwlExStyle,
                new IntPtr(extendedStyle | WsExToolWindow | WsExNoActivate));
            Reposition();
        };

        _positionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _positionTimer.Tick += (_, _) => Reposition();
    }

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
            ToolTip = "Codex usage is currently unavailable.";
            return;
        }

        var value = Math.Round(Math.Clamp(remainingPercent.Value, 0d, 100d));
        UsageText.Text = $"{value:0}%";
        ToolTip = resetsAt is null
            ? $"Codex: {value:0}% remaining"
            : $"Codex: {value:0}% remaining · resets {resetsAt.Value:ddd HH:mm}";
    }

    public void Reposition()
    {
        if (_windowHandle == IntPtr.Zero || !IsVisible)
        {
            return;
        }

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var taskbarRect))
        {
            return;
        }

        var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var trayLeft = tray != IntPtr.Zero && GetWindowRect(tray, out var trayRect)
            ? trayRect.Left
            : taskbarRect.Right - 240;

        var dpi = GetDpiForWindow(taskbar);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var width = (int)Math.Round(94d * scale);
        var height = (int)Math.Round(40d * scale);
        var gap = 0;
        var left = trayLeft - width - gap;
        var top = taskbarRect.Top + Math.Max(0, (taskbarRect.Bottom - taskbarRect.Top - height) / 2);

        SetWindowPos(
            _windowHandle,
            HwndTopmost,
            left,
            top,
            width,
            height,
            SwpNoActivate | SwpShowWindow);
    }

    private void LabelSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OpenMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void DesktopModeMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        DesktopModeRequested?.Invoke(this, EventArgs.Empty);

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string className,
        string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
