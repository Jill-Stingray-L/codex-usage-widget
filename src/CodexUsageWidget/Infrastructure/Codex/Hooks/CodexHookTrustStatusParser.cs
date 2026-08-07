using System.Text.Json;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

internal static class CodexHookTrustStatusParser
{
    private static readonly string[] RequiredEvents = ["userPromptSubmit", "stop", "sessionEnd"];

    public static CodexHookTrustEvaluation Parse(JsonElement result, string expectedCommand)
    {
        if (!result.TryGetProperty("data", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return CodexHookTrustEvaluation.Unavailable;
        }

        var matchingEntries = entries
            .EnumerateArray()
            .Where(entry => ReadHooks(entry).Any(hook => IsExpectedCommand(hook, expectedCommand)))
            .ToArray();
        if (matchingEntries.Any(HasConfigurationErrors))
        {
            return CodexHookTrustEvaluation.Unavailable;
        }

        var matchingHooks = matchingEntries
            .SelectMany(ReadHooks)
            .Where(hook => IsExpectedCommand(hook, expectedCommand))
            .ToArray();

        var requiredHooks = RequiredEvents
            .Select(eventName => matchingHooks.FirstOrDefault(hook =>
                string.Equals(ReadString(hook, "eventName"), eventName, StringComparison.Ordinal)))
            .ToArray();
        if (requiredHooks.Any(hook => hook.ValueKind == JsonValueKind.Undefined))
        {
            return CodexHookTrustEvaluation.Unavailable;
        }

        if (requiredHooks.Any(hook => !HasMatchAllMatcher(hook)))
        {
            return CodexHookTrustEvaluation.Unavailable;
        }

        var statuses = requiredHooks
            .Select(hook => ReadString(hook, "trustStatus"))
            .ToArray();
        if (statuses.Any(status => string.Equals(status, "modified", StringComparison.Ordinal)))
        {
            return CodexHookTrustEvaluation.Modified;
        }

        if (statuses.Any(status => string.Equals(status, "untrusted", StringComparison.Ordinal)))
        {
            return CodexHookTrustEvaluation.ApprovalRequired;
        }

        if (requiredHooks.Any(hook => !ReadBoolean(hook, "enabled")))
        {
            return CodexHookTrustEvaluation.Unavailable;
        }

        return statuses.All(status =>
            string.Equals(status, "trusted", StringComparison.Ordinal) ||
            string.Equals(status, "managed", StringComparison.Ordinal))
            ? CodexHookTrustEvaluation.Active
            : CodexHookTrustEvaluation.Unavailable;
    }

    private static IEnumerable<JsonElement> ReadHooks(JsonElement entry)
    {
        if (!entry.TryGetProperty("hooks", out var hooks) ||
            hooks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return hooks.EnumerateArray().Select(hook => hook.Clone());
    }

    private static bool IsExpectedCommand(JsonElement hook, string expectedCommand) =>
        ReadString(hook, "command") is { } command &&
        string.Equals(command, expectedCommand, StringComparison.Ordinal);

    private static bool HasConfigurationErrors(JsonElement entry) =>
        entry.TryGetProperty("errors", out var errors) &&
        errors.ValueKind == JsonValueKind.Array &&
        errors.GetArrayLength() > 0;

    private static bool HasMatchAllMatcher(JsonElement hook)
    {
        if (!hook.TryGetProperty("matcher", out var matcher) ||
            matcher.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return matcher.ValueKind == JsonValueKind.String &&
            matcher.GetString() is "" or "*";
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;
}

internal enum CodexHookTrustEvaluation
{
    Unavailable,
    ApprovalRequired,
    Active,
    Modified
}
