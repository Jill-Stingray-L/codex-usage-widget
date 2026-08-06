using System.Runtime.InteropServices;

namespace CodexUsageWidget.Infrastructure.Windows;

public sealed class WindowChangeWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint GetAncestorRoot = 2;
    private const int ObjectIdWindow = 0;

    private readonly Action _windowChanged;
    private readonly WinEventCallback _callback;
    private readonly IntPtr _foregroundHook;
    private readonly IntPtr _locationHook;
    private bool _disposed;

    public WindowChangeWatcher(Action windowChanged)
    {
        ArgumentNullException.ThrowIfNull(windowChanged);

        _windowChanged = windowChanged;
        _callback = OnWinEvent;
        _foregroundHook = CreateHook(EventSystemForeground);
        _locationHook = CreateHook(EventObjectLocationChange);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseHook(_foregroundHook);
        ReleaseHook(_locationHook);
        GC.SuppressFinalize(this);
    }

    private IntPtr CreateHook(uint eventType) =>
        SetWinEventHook(
            eventType,
            eventType,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime)
    {
        if (_disposed || windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (eventType == EventObjectLocationChange &&
            (objectId != ObjectIdWindow ||
             childId != 0 ||
             !IsRelevantLocationChange(windowHandle)))
        {
            return;
        }

        _windowChanged();
    }

    private static bool IsRelevantLocationChange(IntPtr windowHandle)
    {
        if (windowHandle == GetForegroundWindow())
        {
            return true;
        }

        var taskbar = FindWindow("Shell_TrayWnd", null);
        return taskbar != IntPtr.Zero &&
               (windowHandle == taskbar || GetAncestor(windowHandle, GetAncestorRoot) == taskbar);
    }

    private static void ReleaseHook(IntPtr hook)
    {
        if (hook != IntPtr.Zero && !UnhookWinEvent(hook))
        {
            // There is no safe recovery action during window teardown.
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr eventHookModule,
        WinEventCallback eventHookCallback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr eventHook);

    private delegate void WinEventCallback(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime);
}
