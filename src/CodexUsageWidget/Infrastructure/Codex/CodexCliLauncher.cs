using System.Diagnostics;
using CodexUsageWidget.Application;

namespace CodexUsageWidget.Infrastructure.Codex;

public sealed class CodexCliLauncher : ICodexLauncher
{
    public void OpenInteractive()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            WindowStyle = ProcessWindowStyle.Normal
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/k");
        startInfo.ArgumentList.Add("call");
        startInfo.ArgumentList.Add(CodexExecutableLocator.Resolve());

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Windows could not open Codex CLI.");
        }
    }
}
