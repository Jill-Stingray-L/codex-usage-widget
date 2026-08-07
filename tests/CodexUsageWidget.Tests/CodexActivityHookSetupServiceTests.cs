using System.Text.Json;
using CodexUsageWidget.Application;
using CodexUsageWidget.Infrastructure.Codex;
using CodexUsageWidget.Infrastructure.Codex.Hooks;

namespace CodexUsageWidget.Tests;

public sealed class CodexActivityHookSetupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexUsageWidget.Tests",
        Guid.NewGuid().ToString("N"));

    private string HooksPath => Path.Combine(_directory, "hooks.json");

    private string ConfigPath => Path.Combine(_directory, "config.toml");

    private string WidgetPath => Path.Combine(_directory, "CodexUsageWidget.exe");

    [Fact]
    public async Task MissingConfigurationIsReportedWithoutStartingAppServer()
    {
        var session = new FakeAppServerSession();
        var service = CreateService(session);

        var status = await service.GetStatusAsync();

        Assert.Equal(ActivityHookSetupState.NotInstalled, status.State);
        Assert.Equal(0, session.RequestCount);
    }

    [Theory]
    [InlineData("trusted", "trusted", "trusted", ActivityHookSetupState.Active)]
    [InlineData("managed", "trusted", "trusted", ActivityHookSetupState.Active)]
    [InlineData("trusted", "untrusted", "trusted", ActivityHookSetupState.ApprovalRequired)]
    [InlineData("trusted", "trusted", "modified", ActivityHookSetupState.Modified)]
    public async Task InstalledConfigurationUsesCodexTrustStatus(
        string promptStatus,
        string stopStatus,
        string sessionStatus,
        ActivityHookSetupState expectedState)
    {
        var session = new FakeAppServerSession();
        var manager = CreateManager();
        manager.Apply(manager.PlanInstall(WidgetPath));
        session.Result = CreateHooksListResult(
            CodexHookConfigurationManager.BuildHookCommand(WidgetPath),
            promptStatus,
            stopStatus,
            sessionStatus);
        var service = CreateService(session);

        var status = await service.GetStatusAsync();

        Assert.Equal(expectedState, status.State);
        Assert.Equal("hooks/list", session.LastMethod);
    }

    [Fact]
    public async Task MissingRuntimeHookIsReportedAsInstalledWithUnknownStatus()
    {
        var session = new FakeAppServerSession
        {
            Result = ParseElement("""{ "data": [{ "cwd": "C:\\\\work", "hooks": [] }] }""")
        };
        var manager = CreateManager();
        manager.Apply(manager.PlanInstall(WidgetPath));

        var status = await CreateService(session).GetStatusAsync();

        Assert.Equal(ActivityHookSetupState.InstalledStatusUnavailable, status.State);
    }

    [Fact]
    public async Task ExplicitlyDisabledHooksHaveDedicatedStatus()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "[features]\nhooks = false\n");

        var status = await CreateService(new FakeAppServerSession()).GetStatusAsync();

        Assert.Equal(ActivityHookSetupState.HooksDisabled, status.State);
        Assert.Contains("explicitly disabled", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallAppliesTheReviewedContent()
    {
        var service = CreateService(new FakeAppServerSession());
        var preview = service.PrepareChange(ActivityHookChangeKind.Install);

        service.ApplyChange(preview);

        Assert.Equal(preview.ProposedContent, File.ReadAllText(HooksPath));
    }

    [Fact]
    public void ChangedConfigurationMustBeReviewedAgain()
    {
        var service = CreateService(new FakeAppServerSession());
        var preview = service.PrepareChange(ActivityHookChangeKind.Install);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HooksPath, """{ "description": "changed" }""");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.ApplyChange(preview));

        Assert.Contains("changed after the preview", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("UserPromptSubmit", File.ReadAllText(HooksPath), StringComparison.Ordinal);
    }

    private CodexActivityHookSetupService CreateService(FakeAppServerSession session) =>
        new(CreateManager(), session, WidgetPath, _directory);

    private CodexHookConfigurationManager CreateManager() => new(HooksPath, ConfigPath);

    private static JsonElement CreateHooksListResult(
        string command,
        string promptStatus,
        string stopStatus,
        string sessionStatus)
    {
        var serializedCommand = JsonSerializer.Serialize(command);
        return ParseElement($$"""
            {
              "data": [{
                "cwd": "C:\\work",
                "hooks": [
                  { "eventName": "userPromptSubmit", "command": {{serializedCommand}}, "trustStatus": "{{promptStatus}}" },
                  { "eventName": "stop", "command": {{serializedCommand}}, "trustStatus": "{{stopStatus}}" },
                  { "eventName": "sessionEnd", "command": {{serializedCommand}}, "trustStatus": "{{sessionStatus}}" }
                ]
              }]
            }
            """);
    }

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeAppServerSession : ICodexAppServerSession
    {
        public event EventHandler<string>? NotificationReceived;

        public event EventHandler<string>? DiagnosticMessage;

        public JsonElement Result { get; set; } = ParseElement("""{ "data": [] }""");

        public int RequestCount { get; private set; }

        public string? LastMethod { get; private set; }

        public Task<JsonElement> RequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = method;
            return Task.FromResult(Result);
        }

        public ValueTask DisposeAsync()
        {
            GC.KeepAlive(NotificationReceived);
            GC.KeepAlive(DiagnosticMessage);
            return ValueTask.CompletedTask;
        }
    }
}
