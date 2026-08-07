using System.IO;
using System.Text;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public static class CodexActivityHookCommand
{
    private static readonly byte[] NeutralResult = Encoding.UTF8.GetBytes("{\"continue\":true}\n");

    public static async Task<int> RunAsync(
        Stream input,
        Stream output,
        string pipeName = CodexActivityPipeClient.DefaultPipeName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var signal = await CodexActivityHookPayloadParser.ParseAsync(input, cancellationToken)
                .ConfigureAwait(false);
            if (signal is not null)
            {
                await CodexActivityPipeClient.TrySendAsync(
                    signal,
                    pipeName,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Activity reporting must never interfere with Codex.
        }

        try
        {
            await output.WriteAsync(NeutralResult, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A closed stdout must not turn this advisory hook into a failure.
        }

        return 0;
    }
}
