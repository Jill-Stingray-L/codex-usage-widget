using CodexUsageWidget.Infrastructure.Settings;

namespace CodexUsageWidget.Tests;

public sealed class DisplayModeStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexUsageWidget.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadUsesDesktopWidgetWhenPreferenceDoesNotExist()
    {
        var store = new DisplayModeStore(Path.Combine(_directory, "display-mode.txt"));

        Assert.Equal(WidgetDisplayMode.DesktopWidget, store.Load());
    }

    [Fact]
    public void SavePersistsTaskbarPreference()
    {
        var store = new DisplayModeStore(Path.Combine(_directory, "display-mode.txt"));

        store.Save(WidgetDisplayMode.TaskbarIndicator);

        Assert.Equal(WidgetDisplayMode.TaskbarIndicator, store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
