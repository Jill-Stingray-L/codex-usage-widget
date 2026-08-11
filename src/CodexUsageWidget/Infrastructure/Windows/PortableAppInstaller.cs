using System.IO;
using System.Security.Cryptography;

namespace CodexUsageWidget.Infrastructure.Windows;

public static class PortableAppInstaller
{
    private const string ExecutableName = "CodexUsageWidget.exe";

    public static bool TryInstallFromTemporaryLocation(
        string currentExecutablePath,
        string temporaryRoot,
        string localDataDirectory,
        string version,
        out string installedExecutablePath) =>
        TryInstallFromTemporaryLocation(
            currentExecutablePath,
            [temporaryRoot],
            localDataDirectory,
            version,
            out installedExecutablePath);

    public static bool TryInstallFromTemporaryLocation(
        string currentExecutablePath,
        IReadOnlyCollection<string> temporaryRoots,
        string localDataDirectory,
        string version,
        out string installedExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExecutablePath);
        ArgumentNullException.ThrowIfNull(temporaryRoots);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (temporaryRoots.Count == 0)
        {
            throw new ArgumentException("At least one temporary root is required.", nameof(temporaryRoots));
        }

        if (!IsSafePathSegment(version))
        {
            throw new ArgumentException("The application version is not a safe path segment.", nameof(version));
        }

        var sourcePath = Path.GetFullPath(currentExecutablePath);
        installedExecutablePath = sourcePath;
        if (!temporaryRoots.Any(root => IsDescendantOf(sourcePath, root)))
        {
            return false;
        }

        var installDirectory = Path.Combine(
            Path.GetFullPath(localDataDirectory),
            "app",
            version);
        var targetPath = Path.Combine(installDirectory, ExecutableName);
        Directory.CreateDirectory(installDirectory);
        if (!FilesHaveSameContent(sourcePath, targetPath))
        {
            CopyAtomically(sourcePath, targetPath);
        }

        installedExecutablePath = targetPath;
        return true;
    }

    private static bool IsDescendantOf(string path, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var directoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var relativePath = Path.GetRelativePath(directoryPath, path);
        return !Path.IsPathRooted(relativePath) &&
               !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsSafePathSegment(string value) =>
        value is not "." and not ".." &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains(Path.DirectorySeparatorChar) &&
        !value.Contains(Path.AltDirectorySeparatorChar);

    private static bool FilesHaveSameContent(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return false;
        }

        var sourceInfo = new FileInfo(sourcePath);
        var targetInfo = new FileInfo(targetPath);
        if (sourceInfo.Length != targetInfo.Length)
        {
            return false;
        }

        using var source = File.OpenRead(sourcePath);
        using var target = File.OpenRead(targetPath);
        return SHA256.HashData(source).AsSpan().SequenceEqual(SHA256.HashData(target));
    }

    private static void CopyAtomically(string sourcePath, string targetPath)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
