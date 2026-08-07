using CodexUsageWidget.Infrastructure.Codex.Hooks;

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

        var application = new App();
        application.InitializeComponent();
        return application.Run();
    }
}
