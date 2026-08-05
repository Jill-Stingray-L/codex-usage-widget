using System.IO;

namespace CodexUsageWidget.Infrastructure.Settings;

public sealed class DisplayModeStore
{
    private readonly string _path;

    public DisplayModeStore(string? path = null)
    {
        _path = path ?? AppPaths.DisplayModeFile;
    }

    public WidgetDisplayMode Load()
    {
        try
        {
            var value = File.ReadAllText(_path).Trim();
            return value.Equals("taskbar", StringComparison.OrdinalIgnoreCase)
                ? WidgetDisplayMode.TaskbarIndicator
                : WidgetDisplayMode.DesktopWidget;
        }
        catch (IOException)
        {
            return WidgetDisplayMode.DesktopWidget;
        }
        catch (UnauthorizedAccessException)
        {
            return WidgetDisplayMode.DesktopWidget;
        }
    }

    public void Save(WidgetDisplayMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                _path,
                mode == WidgetDisplayMode.TaskbarIndicator ? "taskbar" : "widget");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
