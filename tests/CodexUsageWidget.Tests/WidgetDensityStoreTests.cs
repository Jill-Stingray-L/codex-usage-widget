using CodexUsageWidget.Infrastructure.Settings;

namespace CodexUsageWidget.Tests;

public sealed class WidgetDensityStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexUsageWidget.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadUsesCompactWhenPreferenceDoesNotExist()
    {
        var store = new WidgetDensityStore(Path.Combine(_directory, "widget-density.txt"));

        Assert.Equal(WidgetDensity.Compact, store.Load());
    }

    [Fact]
    public void SavePersistsDetailedPreference()
    {
        var store = new WidgetDensityStore(Path.Combine(_directory, "widget-density.txt"));

        store.Save(WidgetDensity.Detailed);

        Assert.Equal(WidgetDensity.Detailed, store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
