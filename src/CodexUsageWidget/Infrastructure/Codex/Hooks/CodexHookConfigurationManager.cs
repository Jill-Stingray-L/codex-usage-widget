using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public sealed partial class CodexHookConfigurationManager
{
    private const int HookTimeoutSeconds = 3;
    private const int LegacyHookTimeoutSeconds = 1;
    private const string HookArgument = "--codex-activity-hook";
    private const string WidgetExecutableName = "CodexUsageWidget.exe";
    private const string NestedPowerShellPrefix =
        "powershell.exe -NoLogo -NoProfile -NonInteractive " +
        "-ExecutionPolicy Bypass -Command \"& '";
    private static readonly string[] ActivityEvents = ["UserPromptSubmit", "Stop", "SessionEnd"];
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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
            return ErrorPlan(
                "The widget executable path must be absolute.",
                CodexHookConfigurationErrorKind.InvalidProcessPath);
        }

        var featureError = GetDisabledFeatureError();
        if (featureError is not null)
        {
            return ErrorPlan(featureError, CodexHookConfigurationErrorKind.HooksDisabled);
        }

        return PlanChange(processPath, install: true);
    }

    public CodexHookConfigurationPlan PlanUninstall(string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath))
        {
            return ErrorPlan(
                "The widget executable path must be absolute.",
                CodexHookConfigurationErrorKind.InvalidProcessPath);
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
                CodexHookConfigurationErrorKind.InvalidConfiguration,
                originalExisted,
                originalContent);
        }

        if (root["hooks"] is not null && root["hooks"] is not JsonObject)
        {
            return ErrorPlan(
                $"Cannot safely modify '{_hooksPath}': 'hooks' is not a JSON object.",
                CodexHookConfigurationErrorKind.InvalidConfiguration,
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
        foreach (var eventName in ActivityEvents)
        {
            if (hooks[eventName] is not null && hooks[eventName] is not JsonArray)
            {
                return ErrorPlan(
                    $"Cannot safely modify '{_hooksPath}': hooks.{eventName} is not a JSON array.",
                    CodexHookConfigurationErrorKind.InvalidConfiguration,
                    originalExisted,
                    originalContent);
            }

            var groups = hooks[eventName] as JsonArray;
            if (install)
            {
                groups ??= new JsonArray();
                hooks[eventName] = groups;
                var recognizedCount = CountRecognizedHandlers(groups);
                var currentCount = CountExactHandlers(groups, command);
                if (recognizedCount != 1 || currentCount != 1)
                {
                    RemoveRecognizedHandlers(groups);
                    groups.Add(new JsonObject
                    {
                        ["hooks"] = new JsonArray(CreateHandler(command))
                    });
                    changed = true;
                }
            }
            else if (groups is not null)
            {
                changed |= RemoveRecognizedHandlers(groups);
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
            originalContent,
            CodexHookConfigurationErrorKind.None);
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

    private static int CountExactHandlers(JsonArray groups, string command)
    {
        var expected = CreateHandler(command);
        return groups
            .OfType<JsonObject>()
            .Select(group => group["hooks"])
            .OfType<JsonArray>()
            .SelectMany(handlers => handlers)
            .Count(handler => JsonNode.DeepEquals(handler, expected));
    }

    private static int CountRecognizedHandlers(JsonArray groups) =>
        groups
            .OfType<JsonObject>()
            .Select(group => group["hooks"])
            .OfType<JsonArray>()
            .SelectMany(handlers => handlers)
            .Count(IsRecognizedWidgetHandler);

    private static bool RemoveRecognizedHandlers(JsonArray groups)
    {
        var changed = false;
        for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
        {
            if (groups[groupIndex] is not JsonObject group ||
                group["hooks"] is not JsonArray handlers)
            {
                continue;
            }

            var removedFromGroup = false;
            for (var index = handlers.Count - 1; index >= 0; index--)
            {
                if (IsRecognizedWidgetHandler(handlers[index]))
                {
                    handlers.RemoveAt(index);
                    changed = true;
                    removedFromGroup = true;
                }
            }

            if (removedFromGroup && handlers.Count == 0 && group.Count == 1)
            {
                groups.RemoveAt(groupIndex);
            }
        }

        return changed;
    }

    private static bool IsRecognizedWidgetHandler(JsonNode? handler)
    {
        if (handler is not JsonObject handlerObject ||
            handlerObject["command"] is not JsonValue commandValue ||
            !commandValue.TryGetValue<string>(out var command) ||
            !TryGetGeneratedWidgetCommandTimeout(command, out var timeoutSeconds))
        {
            return false;
        }

        return JsonNode.DeepEquals(handler, CreateHandler(command, timeoutSeconds));
    }

    private static bool TryGetGeneratedWidgetCommandTimeout(
        string command,
        out int timeoutSeconds)
    {
        if (TryReadSingleQuotedPath(
                command,
                "& '",
                $"' {HookArgument}",
                out var processPath) ||
            TryReadSingleQuotedPath(
                command,
                NestedPowerShellPrefix,
                $"' {HookArgument}\"",
                out processPath))
        {
            timeoutSeconds = HookTimeoutSeconds;
            return IsWidgetExecutablePath(processPath);
        }

        const string LegacyPrefix = "\"";
        var legacySuffix = $"\" {HookArgument}";
        if (command.StartsWith(LegacyPrefix, StringComparison.Ordinal) &&
            command.EndsWith(legacySuffix, StringComparison.Ordinal) &&
            command.Length > LegacyPrefix.Length + legacySuffix.Length)
        {
            var pathLength = command.Length - LegacyPrefix.Length - legacySuffix.Length;
            processPath = command.Substring(LegacyPrefix.Length, pathLength);
            if (!processPath.Contains('"', StringComparison.Ordinal))
            {
                timeoutSeconds = LegacyHookTimeoutSeconds;
                return IsWidgetExecutablePath(processPath);
            }
        }

        timeoutSeconds = 0;
        return false;
    }

    private static bool TryReadSingleQuotedPath(
        string command,
        string prefix,
        string suffix,
        out string processPath)
    {
        processPath = string.Empty;
        if (!command.StartsWith(prefix, StringComparison.Ordinal) ||
            !command.EndsWith(suffix, StringComparison.Ordinal) ||
            command.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var escapedPath = command.AsSpan(
            prefix.Length,
            command.Length - prefix.Length - suffix.Length);
        var path = new StringBuilder(escapedPath.Length);
        for (var index = 0; index < escapedPath.Length; index++)
        {
            if (escapedPath[index] != '\'')
            {
                path.Append(escapedPath[index]);
                continue;
            }

            if (index + 1 >= escapedPath.Length || escapedPath[index + 1] != '\'')
            {
                return false;
            }

            path.Append('\'');
            index++;
        }

        processPath = path.ToString();
        return true;
    }

    private static bool IsWidgetExecutablePath(string processPath) =>
        Path.IsPathFullyQualified(processPath) &&
        string.Equals(
            Path.GetFileName(processPath),
            WidgetExecutableName,
            StringComparison.OrdinalIgnoreCase);

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
            originalContent,
            CodexHookConfigurationErrorKind.None);

    private static CodexHookConfigurationPlan ErrorPlan(
        string error,
        CodexHookConfigurationErrorKind errorKind,
        bool originalExisted = false,
        string? originalContent = null) =>
        new(
            hasChanges: false,
            proposedContent: originalContent ?? string.Empty,
            error,
            originalExisted,
            originalContent,
            errorKind);

    [GeneratedRegex(@"^\s*\[[^\]]+\]\s*(?:#.*)?$")]
    private static partial Regex TomlSectionRegex();

    [GeneratedRegex(@"^\s*\[\s*features\s*\]\s*(?:#.*)?$")]
    private static partial Regex FeaturesSectionRegex();

    [GeneratedRegex("^\\s*(?:hooks|\"hooks\")\\s*=\\s*false\\s*(?:#.*)?$")]
    private static partial Regex DisabledHooksRegex();
}

public enum CodexHookConfigurationErrorKind
{
    None,
    HooksDisabled,
    InvalidProcessPath,
    InvalidConfiguration
}

public sealed class CodexHookConfigurationPlan
{
    internal CodexHookConfigurationPlan(
        bool hasChanges,
        string proposedContent,
        string? error,
        bool originalExisted,
        string? originalContent,
        CodexHookConfigurationErrorKind errorKind)
    {
        HasChanges = hasChanges;
        ProposedContent = proposedContent;
        Error = error;
        OriginalExisted = originalExisted;
        OriginalContent = originalContent;
        ErrorKind = errorKind;
    }

    public bool HasChanges { get; }

    public string ProposedContent { get; }

    public string? Error { get; }

    public CodexHookConfigurationErrorKind ErrorKind { get; }

    internal bool OriginalExisted { get; }

    internal string? OriginalContent { get; }
}
