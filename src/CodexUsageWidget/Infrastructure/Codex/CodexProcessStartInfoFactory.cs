using System.Diagnostics;
using System.Text;

namespace CodexUsageWidget.Infrastructure.Codex;

internal static class CodexProcessStartInfoFactory
{
    public static ProcessStartInfo Create()
    {
        var executable = CodexExecutableLocator.Resolve();
        var isCommandScript = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                              executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

        var info = isCommandScript
            ? CreateCommandScriptStartInfo(executable)
            : new ProcessStartInfo(executable);

        if (!isCommandScript)
        {
            info.ArgumentList.Add("app-server");
        }

        info.UseShellExecute = false;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        info.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        return info;
    }

    private static ProcessStartInfo CreateCommandScriptStartInfo(string executable)
    {
        var info = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe");
        info.ArgumentList.Add("/d");
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("call");
        info.ArgumentList.Add(executable);
        info.ArgumentList.Add("app-server");
        return info;
    }
}
