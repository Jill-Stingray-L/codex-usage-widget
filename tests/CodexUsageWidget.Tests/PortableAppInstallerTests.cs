using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Tests;

public sealed class PortableAppInstallerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexUsageWidget.Tests",
        Guid.NewGuid().ToString("N"));

    private string TemporaryRoot => Path.Combine(_directory, "Temp");

    private string LocalDataRoot => Path.Combine(_directory, "LocalData");

    [Fact]
    public void TemporaryExecutableIsCopiedToVersionedPerUserLocation()
    {
        var source = CreateExecutable(Path.Combine(TemporaryRoot, "archive.zip.tmp"), [1, 2, 3]);

        var installed = PortableAppInstaller.TryInstallFromTemporaryLocation(
            source,
            TemporaryRoot,
            LocalDataRoot,
            "1.4.0",
            out var installedPath);

        Assert.True(installed);
        Assert.Equal(
            Path.Combine(LocalDataRoot, "app", "1.4.0", "CodexUsageWidget.exe"),
            installedPath);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(installedPath));
    }

    [Fact]
    public void ExecutableOutsideTemporaryRootIsLeftInPlace()
    {
        var source = CreateExecutable(Path.Combine(_directory, "Portable"), [1]);

        var installed = PortableAppInstaller.TryInstallFromTemporaryLocation(
            source,
            TemporaryRoot,
            LocalDataRoot,
            "1.4.0",
            out var installedPath);

        Assert.False(installed);
        Assert.Equal(source, installedPath);
        Assert.False(Directory.Exists(LocalDataRoot));
    }

    [Fact]
    public void RepeatedLaunchReusesIdenticalInstalledExecutable()
    {
        var source = CreateExecutable(Path.Combine(TemporaryRoot, "archive.zip.tmp"), [4, 5, 6]);
        PortableAppInstaller.TryInstallFromTemporaryLocation(
            source,
            TemporaryRoot,
            LocalDataRoot,
            "1.4.0",
            out var firstPath);
        var originalWriteTime = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(firstPath, originalWriteTime);

        var installed = PortableAppInstaller.TryInstallFromTemporaryLocation(
            source,
            TemporaryRoot,
            LocalDataRoot,
            "1.4.0",
            out var secondPath);

        Assert.True(installed);
        Assert.Equal(firstPath, secondPath);
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(secondPath));
    }

    private static string CreateExecutable(string directory, byte[] content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "CodexUsageWidget.exe");
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
