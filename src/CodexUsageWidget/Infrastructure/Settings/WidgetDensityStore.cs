using System.IO;

namespace CodexUsageWidget.Infrastructure.Settings;

public sealed class WidgetDensityStore
{
    private readonly string _path;

    public WidgetDensityStore(string? path = null)
    {
        _path = path ?? AppPaths.WidgetDensityFile;
    }

    public WidgetDensity Load()
    {
        try
        {
            var value = File.ReadAllText(_path).Trim();
            return value.Equals("detailed", StringComparison.OrdinalIgnoreCase)
                ? WidgetDensity.Detailed
                : WidgetDensity.Compact;
        }
        catch (IOException)
        {
            return WidgetDensity.Compact;
        }
        catch (UnauthorizedAccessException)
        {
            return WidgetDensity.Compact;
        }
    }

    public void Save(WidgetDensity density)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                _path,
                density == WidgetDensity.Detailed ? "detailed" : "compact");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
