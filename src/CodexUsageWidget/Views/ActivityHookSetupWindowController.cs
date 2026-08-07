using System.Windows;
using CodexUsageWidget.Application;

namespace CodexUsageWidget.Views;

public sealed class ActivityHookSetupWindowController
{
    private readonly Window _owner;
    private readonly IActivityHookSetupService _setupService;
    private readonly ICodexLauncher _codexLauncher;
    private ActivityHookSetupWindow? _window;

    public ActivityHookSetupWindowController(
        Window owner,
        IActivityHookSetupService setupService,
        ICodexLauncher codexLauncher)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(setupService);
        ArgumentNullException.ThrowIfNull(codexLauncher);
        _owner = owner;
        _setupService = setupService;
        _codexLauncher = codexLauncher;
    }

    public event EventHandler? Closed;

    public bool IsOpen => _window is not null;

    public void Show()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var window = new ActivityHookSetupWindow(_setupService, _codexLauncher)
        {
            Owner = _owner
        };
        _window = window;
        window.Closed += WindowOnClosed;
        window.Show();
    }

    private void WindowOnClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            _window.Closed -= WindowOnClosed;
            _window = null;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }
}
