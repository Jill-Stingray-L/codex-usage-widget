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

        var matchingHooks = entries
            .EnumerateArray()
            .SelectMany(ReadHooks)
            .Where(hook =>
                ReadString(hook, "command") is { } command &&
                string.Equals(command, expectedCommand, StringComparison.Ordinal))
            .ToArray();

        var requiredHooks = RequiredEvents
            .Select(eventName => matchingHooks.FirstOrDefault(hook =>
                string.Equals(ReadString(hook, "eventName"), eventName, StringComparison.Ordinal)))
            .ToArray();
        if (requiredHooks.Any(hook => hook.ValueKind == JsonValueKind.Undefined))
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

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal enum CodexHookTrustEvaluation
{
    Unavailable,
    ApprovalRequired,
    Active,
    Modified
}
