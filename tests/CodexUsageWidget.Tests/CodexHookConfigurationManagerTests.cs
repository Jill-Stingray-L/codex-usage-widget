using System.Text.Json.Nodes;
using CodexUsageWidget.Infrastructure.Codex.Hooks;

namespace CodexUsageWidget.Tests;

public sealed class CodexHookConfigurationManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexUsageWidget.Tests",
        Guid.NewGuid().ToString("N"));

    private string HooksPath => Path.Combine(_directory, "hooks.json");

    private string ConfigPath => Path.Combine(_directory, "config.toml");

    [Fact]
    public void InstallCreatesNewHooksFile()
    {
        var manager = CreateManager();

        var plan = manager.PlanInstall(WidgetPath());
        manager.Apply(plan);

        var hooks = JsonNode.Parse(File.ReadAllText(HooksPath))!["hooks"]!.AsObject();
        Assert.Single(hooks["UserPromptSubmit"]!.AsArray());
        Assert.Single(hooks["Stop"]!.AsArray());
        Assert.Single(hooks["SessionEnd"]!.AsArray());
        var content = File.ReadAllText(HooksPath);
        Assert.Contains("& '", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0026", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallMergesExistingHooksAndUnknownFields()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HooksPath, """
            {
              "description": "keep me",
              "unknown": { "value": 42 },
              "hooks": {
                "PreToolUse": [{ "hooks": [{ "type": "command", "command": "other" }] }]
              }
            }
            """);
        var manager = CreateManager();

        manager.Apply(manager.PlanInstall(WidgetPath()));

        var root = JsonNode.Parse(File.ReadAllText(HooksPath))!.AsObject();
        Assert.Equal("keep me", root["description"]!.GetValue<string>());
        Assert.Equal(42, root["unknown"]!["value"]!.GetValue<int>());
        Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());
    }

    [Fact]
    public void RepeatedInstallIsIdempotent()
    {
        var manager = CreateManager();
        manager.Apply(manager.PlanInstall(WidgetPath()));

        var secondPlan = manager.PlanInstall(WidgetPath());

        Assert.False(secondPlan.HasChanges);
        Assert.Null(secondPlan.Error);
    }

    [Fact]
    public void UninstallRemovesOnlyExactWidgetHandlers()
    {
        var manager = CreateManager();
        manager.Apply(manager.PlanInstall(WidgetPath()));
        var root = JsonNode.Parse(File.ReadAllText(HooksPath))!.AsObject();
        root["hooks"]!["Stop"]!.AsArray().Add(new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = "other-widget --codex-activity-hook",
                ["timeout"] = 1
            })
        });
        File.WriteAllText(HooksPath, root.ToJsonString());

        manager.Apply(manager.PlanUninstall(WidgetPath()));

        var content = File.ReadAllText(HooksPath);
        Assert.DoesNotContain(
            CodexHookConfigurationManager.BuildHookCommand(WidgetPath()),
            content,
            StringComparison.Ordinal);
        Assert.Contains("other-widget --codex-activity-hook", content, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedExistingJsonRemainsUntouched()
    {
        Directory.CreateDirectory(_directory);
        const string Malformed = "{ not json";
        File.WriteAllText(HooksPath, Malformed);
        var manager = CreateManager();

        var plan = manager.PlanInstall(WidgetPath());

        Assert.NotNull(plan.Error);
        Assert.False(plan.HasChanges);
        Assert.Equal(Malformed, File.ReadAllText(HooksPath));
    }

    [Fact]
    public void HookCommandRunsThroughPowerShellFromAPowerShellHookShell()
    {
        var hookDirectory = Path.Combine(_directory, "Hook's Folder");
        Directory.CreateDirectory(hookDirectory);
        var hookPath = Path.Combine(hookDirectory, "activity hook.cmd");
        File.WriteAllText(
            hookPath,
            "@echo off\r\nif not \"%~1\"==\"--codex-activity-hook\" exit /B 7\r\nexit /B 0\r\n");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(CodexHookConfigurationManager.BuildHookCommand(hookPath));

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void HookCommandDoesNotSpawnNestedPowerShell()
    {
        var command = CodexHookConfigurationManager.BuildHookCommand(WidgetPath());

        Assert.StartsWith("& '", command, StringComparison.Ordinal);
        Assert.DoesNotContain("powershell.exe", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallReplacesLegacyDirectExecutableHandlers()
    {
        Directory.CreateDirectory(_directory);
        var legacyCommand = $"\"{WidgetPath()}\" --codex-activity-hook";
        File.WriteAllText(HooksPath, $$"""
            {
              "hooks": {
                "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": {{JsonValue.Create(legacyCommand)!.ToJsonString()}}, "timeout": 1 }] }],
                "Stop": [{ "hooks": [{ "type": "command", "command": {{JsonValue.Create(legacyCommand)!.ToJsonString()}}, "timeout": 1 }] }],
                "SessionEnd": [{ "hooks": [{ "type": "command", "command": {{JsonValue.Create(legacyCommand)!.ToJsonString()}}, "timeout": 1 }] }]
              }
            }
            """);

        var manager = CreateManager();
        manager.Apply(manager.PlanInstall(WidgetPath()));

        var content = File.ReadAllText(HooksPath);
        Assert.DoesNotContain(legacyCommand, content, StringComparison.Ordinal);
        var hooks = JsonNode.Parse(content)!["hooks"]!.AsObject();
        foreach (var eventName in new[] { "UserPromptSubmit", "Stop", "SessionEnd" })
        {
            var groups = hooks[eventName]!.AsArray();
            var handler = groups.Single()!["hooks"]!.AsArray().Single()!;
            Assert.Equal(
                CodexHookConfigurationManager.BuildHookCommand(WidgetPath()),
                handler["command"]!.GetValue<string>());
            Assert.Equal(3, handler["timeout"]!.GetValue<int>());
        }
    }

    [Fact]
    public void InstallReplacesNestedPowerShellHandlers()
    {
        Directory.CreateDirectory(_directory);
        var escapedPath = WidgetPath().Replace("'", "''", StringComparison.Ordinal);
        var nestedCommand = "powershell.exe -NoLogo -NoProfile -NonInteractive " +
            "-ExecutionPolicy Bypass -Command " +
            $"\"& '{escapedPath}' --codex-activity-hook\"";
        File.WriteAllText(HooksPath, $$"""
            {
              "hooks": {
                "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": {{JsonValue.Create(nestedCommand)!.ToJsonString()}}, "timeout": 3 }] }],
                "Stop": [{ "hooks": [{ "type": "command", "command": {{JsonValue.Create(nestedCommand)!.ToJsonString()}}, "timeout": 3 }] }],
                "SessionEnd": [{ "hooks": [{ "type": "command", "command": {{JsonValue.Create(nestedCommand)!.ToJsonString()}}, "timeout": 3 }] }]
              }
            }
            """);

        var manager = CreateManager();
        manager.Apply(manager.PlanInstall(WidgetPath()));

        var content = File.ReadAllText(HooksPath);
        Assert.DoesNotContain("powershell.exe", content, StringComparison.OrdinalIgnoreCase);
        var hooks = JsonNode.Parse(content)!["hooks"]!.AsObject();
        foreach (var eventName in new[] { "UserPromptSubmit", "Stop", "SessionEnd" })
        {
            var handler = hooks[eventName]!.AsArray().Single()!["hooks"]!.AsArray().Single()!;
            Assert.Equal(
                CodexHookConfigurationManager.BuildHookCommand(WidgetPath()),
                handler["command"]!.GetValue<string>());
        }
    }

    [Fact]
    public void ExplicitlyDisabledHooksProduceActionableError()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "[features]\nhooks = false\n");

        var plan = CreateManager().PlanInstall(WidgetPath());

        Assert.Contains("explicitly disabled", plan.Error, StringComparison.Ordinal);
        Assert.Equal(CodexHookConfigurationErrorKind.HooksDisabled, plan.ErrorKind);
        Assert.False(plan.HasChanges);
    }

    private CodexHookConfigurationManager CreateManager() => new(HooksPath, ConfigPath);

    private string WidgetPath() => Path.Combine(_directory, "Widget Folder", "CodexUsageWidget.exe");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
