using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public sealed partial class CodexHookConfigurationManager
{
    private const int HookTimeoutSeconds = 3;
    private const int LegacyHookTimeoutSeconds = 1;
    private static readonly string[] ActivityEvents = ["UserPromptSubmit", "Stop", "SessionEnd"];
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly string _hooksPath;
    private readonly string _configPath;

    public CodexHookConfigurationManager(string? hooksPath = null, string? configPath = null)
    {
        var codexHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        _hooksPath = hooksPath ?? Path.Combine(codexHome, "hooks.json");
        _configPath = configPath ?? Path.Combine(codexHome, "config.toml");
    }

    public CodexHookConfigurationPlan PlanInstall(string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath))
        {
            return ErrorPlan("The widget executable path must be absolute.");
        }

        var featureError = GetDisabledFeatureError();
        if (featureError is not null)
        {
            return ErrorPlan(featureError);
        }

        return PlanChange(processPath, install: true);
    }

    public CodexHookConfigurationPlan PlanUninstall(string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath))
        {
            return ErrorPlan("The widget executable path must be absolute.");
        }

        return PlanChange(processPath, install: false);
    }

    public void Apply(CodexHookConfigurationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Error is not null)
        {
            throw new InvalidOperationException(plan.Error);
        }

        if (!plan.HasChanges)
        {
            return;
        }

        var exists = File.Exists(_hooksPath);
        var currentContent = exists ? File.ReadAllText(_hooksPath) : null;
        if (exists != plan.OriginalExisted ||
            !string.Equals(currentContent, plan.OriginalContent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Codex hooks.json changed after the preview. Review the new file and try again.");
        }

        var directory = Path.GetDirectoryName(_hooksPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_hooksPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, plan.ProposedContent, new UTF8Encoding(false));
            File.Move(temporaryPath, _hooksPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string BuildHookCommand(string processPath)
    {
        var escapedProcessPath = processPath.Replace("'", "''", StringComparison.Ordinal);
        return $"& '{escapedProcessPath}' --codex-activity-hook";
    }

    private static string BuildLegacyHookCommand(string processPath) =>
        $"\"{processPath}\" --codex-activity-hook";

    private static string BuildNestedPowerShellHookCommand(string processPath)
    {
        var escapedProcessPath = processPath.Replace("'", "''", StringComparison.Ordinal);
        return "powershell.exe -NoLogo -NoProfile -NonInteractive " +
            "-ExecutionPolicy Bypass -Command " +
            $"\"& '{escapedProcessPath}' --codex-activity-hook\"";
    }

    private CodexHookConfigurationPlan PlanChange(string processPath, bool install)
    {
        var originalExisted = File.Exists(_hooksPath);
        string? originalContent = null;
        JsonObject root;
        try
        {
            if (originalExisted)
            {
                originalContent = File.ReadAllText(_hooksPath);
                root = JsonNode.Parse(originalContent) as JsonObject ??
                    throw new JsonException("The root value is not an object.");
            }
            else
            {
                root = new JsonObject();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ErrorPlan(
                $"Cannot safely modify '{_hooksPath}': {ex.Message}",
                originalExisted,
                originalContent);
        }

        if (root["hooks"] is not null && root["hooks"] is not JsonObject)
        {
            return ErrorPlan(
                $"Cannot safely modify '{_hooksPath}': 'hooks' is not a JSON object.",
                originalExisted,
                originalContent);
        }

        var hooks = root["hooks"] as JsonObject;
        if (install)
        {
            hooks ??= new JsonObject();
            root["hooks"] = hooks;
        }
        else if (hooks is null)
        {
            return NoChangePlan(originalExisted, originalContent);
        }

        var changed = false;
        var command = BuildHookCommand(processPath);
        var legacyCommand = BuildLegacyHookCommand(processPath);
        var nestedPowerShellCommand = BuildNestedPowerShellHookCommand(processPath);
        foreach (var eventName in ActivityEvents)
        {
            if (hooks[eventName] is not null && hooks[eventName] is not JsonArray)
            {
                return ErrorPlan(
                    $"Cannot safely modify '{_hooksPath}': hooks.{eventName} is not a JSON array.",
                    originalExisted,
                    originalContent);
            }

            var groups = hooks[eventName] as JsonArray;
            if (install)
            {
                groups ??= new JsonArray();
                hooks[eventName] = groups;
                changed |= ReplaceHandlers(
                    groups,
                    legacyCommand,
                    LegacyHookTimeoutSeconds,
                    command);
                changed |= ReplaceHandlers(
                    groups,
                    nestedPowerShellCommand,
                    HookTimeoutSeconds,
                    command);
                if (!ContainsHandler(groups, command))
                {
                    groups.Add(new JsonObject
                    {
                        ["hooks"] = new JsonArray(CreateHandler(command))
                    });
                    changed = true;
                }
            }
            else if (groups is not null)
            {
                changed |= RemoveHandlers(groups, command, HookTimeoutSeconds);
                changed |= RemoveHandlers(
                    groups,
                    legacyCommand,
                    LegacyHookTimeoutSeconds);
                changed |= RemoveHandlers(
                    groups,
                    nestedPowerShellCommand,
                    HookTimeoutSeconds);
            }
        }

        if (!changed)
        {
            return NoChangePlan(originalExisted, originalContent);
        }

        var proposedContent = root.ToJsonString(IndentedJson) + Environment.NewLine;
        return new CodexHookConfigurationPlan(
            hasChanges: true,
            proposedContent,
            error: null,
            originalExisted,
            originalContent);
    }

    private string? GetDisabledFeatureError()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return null;
            }

            var inFeaturesSection = false;
            foreach (var line in File.ReadLines(_configPath))
            {
                if (TomlSectionRegex().IsMatch(line))
                {
                    inFeaturesSection = FeaturesSectionRegex().IsMatch(line);
                    continue;
                }

                if (inFeaturesSection && DisabledHooksRegex().IsMatch(line))
                {
                    return "Codex hooks are explicitly disabled in [features]. " +
                        "Set 'hooks = true' yourself before installing activity hooks.";
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Cannot verify whether Codex hooks are enabled in '{_configPath}': {ex.Message}";
        }
    }

    private static bool ContainsHandler(JsonArray groups, string command)
    {
        var expected = CreateHandler(command);
        return groups
            .OfType<JsonObject>()
            .Select(group => group["hooks"])
            .OfType<JsonArray>()
            .SelectMany(handlers => handlers)
            .Any(handler => JsonNode.DeepEquals(handler, expected));
    }

    private static bool RemoveHandlers(JsonArray groups, string command, int timeoutSeconds)
    {
        var expected = CreateHandler(command, timeoutSeconds);
        var changed = false;
        foreach (var handlers in groups
                     .OfType<JsonObject>()
                     .Select(group => group["hooks"])
                     .OfType<JsonArray>())
        {
            for (var index = handlers.Count - 1; index >= 0; index--)
            {
                if (JsonNode.DeepEquals(handlers[index], expected))
                {
                    handlers.RemoveAt(index);
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool ReplaceHandlers(
        JsonArray groups,
        string oldCommand,
        int oldTimeoutSeconds,
        string newCommand)
    {
        var expected = CreateHandler(oldCommand, oldTimeoutSeconds);
        var replacement = CreateHandler(newCommand);
        var changed = false;
        foreach (var handlers in groups
                     .OfType<JsonObject>()
                     .Select(group => group["hooks"])
                     .OfType<JsonArray>())
        {
            for (var index = 0; index < handlers.Count; index++)
            {
                if (JsonNode.DeepEquals(handlers[index], expected))
                {
                    handlers[index] = replacement.DeepClone();
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static JsonObject CreateHandler(
        string command,
        int timeoutSeconds = HookTimeoutSeconds) => new()
    {
        ["type"] = "command",
        ["command"] = command,
        ["timeout"] = timeoutSeconds
    };

    private static CodexHookConfigurationPlan NoChangePlan(
        bool originalExisted,
        string? originalContent) =>
        new(
            hasChanges: false,
            proposedContent: originalContent ?? string.Empty,
            error: null,
            originalExisted,
            originalContent);

    private static CodexHookConfigurationPlan ErrorPlan(
        string error,
        bool originalExisted = false,
        string? originalContent = null) =>
        new(
            hasChanges: false,
            proposedContent: originalContent ?? string.Empty,
            error,
            originalExisted,
            originalContent);

    [GeneratedRegex(@"^\s*\[[^\]]+\]\s*(?:#.*)?$")]
    private static partial Regex TomlSectionRegex();

    [GeneratedRegex(@"^\s*\[\s*features\s*\]\s*(?:#.*)?$")]
    private static partial Regex FeaturesSectionRegex();

    [GeneratedRegex("^\\s*(?:hooks|\"hooks\")\\s*=\\s*false\\s*(?:#.*)?$")]
    private static partial Regex DisabledHooksRegex();
}

public sealed class CodexHookConfigurationPlan
{
    internal CodexHookConfigurationPlan(
        bool hasChanges,
        string proposedContent,
        string? error,
        bool originalExisted,
        string? originalContent)
    {
        HasChanges = hasChanges;
        ProposedContent = proposedContent;
        Error = error;
        OriginalExisted = originalExisted;
        OriginalContent = originalContent;
    }

    public bool HasChanges { get; }

    public string ProposedContent { get; }

    public string? Error { get; }

    internal bool OriginalExisted { get; }

    internal string? OriginalContent { get; }
}
