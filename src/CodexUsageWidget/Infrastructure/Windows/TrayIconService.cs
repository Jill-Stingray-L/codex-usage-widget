using CodexUsageWidget.Infrastructure.Settings;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget.Infrastructure.Windows;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _desktopWidgetModeItem;
    private readonly Forms.ToolStripMenuItem _taskbarIndicatorModeItem;
    private System.Drawing.Icon _currentIcon;
    private bool _disposed;

    public TrayIconService()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());

        var displayModeMenu = new Forms.ToolStripMenuItem("Display mode");
        _desktopWidgetModeItem = new Forms.ToolStripMenuItem(
            "Desktop widget",
            null,
            (_, _) => DesktopModeRequested?.Invoke(this, EventArgs.Empty));
        _taskbarIndicatorModeItem = new Forms.ToolStripMenuItem(
            "Taskbar label",
            null,
            (_, _) => TaskbarModeRequested?.Invoke(this, EventArgs.Empty));
        displayModeMenu.DropDownItems.Add(_desktopWidgetModeItem);
        displayModeMenu.DropDownItems.Add(_taskbarIndicatorModeItem);
        menu.Items.Add(displayModeMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _currentIcon = UsageIconFactory.Create(null);
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _currentIcon,
            Text = "Codex Usage Widget",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? DesktopModeRequested;

    public event EventHandler? TaskbarModeRequested;

    public event EventHandler? ExitRequested;

    public void SetDisplayMode(WidgetDisplayMode mode)
    {
        _desktopWidgetModeItem.Checked = mode == WidgetDisplayMode.DesktopWidget;
        _taskbarIndicatorModeItem.Checked = mode == WidgetDisplayMode.TaskbarIndicator;
    }

    public void UpdateUsage(double? remainingPercent)
    {
        _notifyIcon.Text = remainingPercent is null
            ? "Codex Usage · unavailable"
            : $"Codex · {Math.Round(remainingPercent.Value):0}% remaining";

        var nextIcon = UsageIconFactory.Create(remainingPercent);
        var previousIcon = _currentIcon;
        _currentIcon = nextIcon;
        _notifyIcon.Icon = nextIcon;
        previousIcon.Dispose();
    }

    private void NotifyIconOnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.MouseClick -= NotifyIconOnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon.Dispose();
    }
}
