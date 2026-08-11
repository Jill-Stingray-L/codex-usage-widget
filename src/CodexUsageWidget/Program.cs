using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using CodexUsageWidget.Infrastructure;
using CodexUsageWidget.Infrastructure.Codex.Hooks;
using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (CodexActivityCommandLine.IsCommandMode(args))
        {
            return CodexActivityCommandLine.RunAsync(args).GetAwaiter().GetResult();
        }

        try
        {
            if (TryRelaunchFromStableLocation(args))
            {
                return 0;
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException or Win32Exception)
        {
            System.Windows.MessageBox.Show(
                "Codex Usage Widget could not install itself for the current user. " + ex.Message,
                "Codex Usage Widget",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return -1;
        }

        var application = new App();
        application.InitializeComponent();
        return application.Run();
    }

    private static bool TryRelaunchFromStableLocation(IReadOnlyCollection<string> arguments)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var temporaryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetTempPath(),
            Path.Combine(localApplicationData, "Temp")
        };
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        if (!PortableAppInstaller.TryInstallFromTemporaryLocation(
                currentPath,
                temporaryRoots,
                AppPaths.LocalDataDirectory,
                version,
                out var installedPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(installedPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installedPath)!
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (Process.Start(startInfo) is null)
        {
            throw new Win32Exception("Windows did not start the installed application.");
        }

        return true;
    }
}
