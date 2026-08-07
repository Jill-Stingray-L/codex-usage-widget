using System.IO;
using System.Text;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public static class CodexActivityCommandLine
{
    private const string HookArgument = "--codex-activity-hook";
    private const string InstallArgument = "--install-activity-hooks";
    private const string UninstallArgument = "--uninstall-activity-hooks";

    public static bool IsCommandMode(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 &&
        arguments[0] is HookArgument or InstallArgument or UninstallArgument;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        await using var inputStream = Console.OpenStandardInput();
        await using var outputStream = Console.OpenStandardOutput();

        if (arguments[0] == HookArgument)
        {
            return await CodexActivityHookCommand.RunAsync(
                inputStream,
                outputStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        using var input = new StreamReader(
            inputStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        await using var output = new StreamWriter(
            outputStream,
            new UTF8Encoding(false),
            leaveOpen: true)
        {
            AutoFlush = true
        };

        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                await output.WriteLineAsync("Cannot determine the widget executable path.")
                    .ConfigureAwait(false);
                return 1;
            }

            var manager = new CodexHookConfigurationManager();
            var install = arguments[0] == InstallArgument;
            var plan = install
                ? manager.PlanInstall(processPath)
                : manager.PlanUninstall(processPath);
            if (plan.Error is not null)
            {
                await output.WriteLineAsync(plan.Error).ConfigureAwait(false);
                return 1;
            }

            if (!plan.HasChanges)
            {
                await output.WriteLineAsync(
                    install
                        ? "Activity hooks are already installed."
                        : "No matching activity hooks are installed.").ConfigureAwait(false);
                return 0;
            }

            await output.WriteLineAsync("Proposed ~/.codex/hooks.json:").ConfigureAwait(false);
            await output.WriteLineAsync(plan.ProposedContent).ConfigureAwait(false);
            await output.WriteAsync("Apply this change? [y/N] ").ConfigureAwait(false);
            var approval = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(approval, "y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(approval, "yes", StringComparison.OrdinalIgnoreCase))
            {
                await output.WriteLineAsync("No changes were made.").ConfigureAwait(false);
                return 0;
            }

            manager.Apply(plan);
            await output.WriteLineAsync(
                install
                    ? "Activity hooks installed. Review and trust the exact definitions with /hooks."
                    : "Matching activity hooks removed.").ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Activity hook configuration failed: {ex.Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }
}
