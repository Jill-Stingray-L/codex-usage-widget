using CodexUsageWidget.Application;
using CodexUsageWidget.Infrastructure.Codex.Hooks;

namespace CodexUsageWidget.Tests;

public sealed class CodexActivityHookPayloadParserTests
{
    [Theory]
    [InlineData("UserPromptSubmit", CodexActivitySignalKind.TurnStarted)]
    [InlineData("Stop", CodexActivitySignalKind.TurnStopped)]
    public void ParsesTurnScopedEvents(string eventName, CodexActivitySignalKind expectedKind)
    {
        var json = $$"""
            {"hook_event_name":"{{eventName}}","session_id":"session-1","turn_id":"turn-1"}
            """;

        var parsed = CodexActivityHookPayloadParser.TryParse(json, out var signal);

        Assert.True(parsed);
        Assert.Equal(expectedKind, signal!.Kind);
        Assert.Equal("session-1", signal.SessionId);
        Assert.Equal("turn-1", signal.TurnId);
    }

    [Fact]
    public void ParsesSessionEndWithoutTurnId()
    {
        const string Json =
            "{\"hook_event_name\":\"SessionEnd\",\"session_id\":\"session-1\"}";

        var parsed = CodexActivityHookPayloadParser.TryParse(Json, out var signal);

        Assert.True(parsed);
        Assert.Equal(CodexActivitySignalKind.SessionEnded, signal!.Kind);
        Assert.Null(signal.TurnId);
    }

    [Theory]
    [InlineData("{\"hook_event_name\":\"Stop\",\"turn_id\":\"turn-1\"}")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":\"session-1\"}")]
    [InlineData("{\"hook_event_name\":\"Unknown\",\"session_id\":\"session-1\",\"turn_id\":\"turn-1\"}")]
    [InlineData("{not json")]
    public void RejectsInvalidOrUnsupportedPayload(string json)
    {
        Assert.False(CodexActivityHookPayloadParser.TryParse(json, out var signal));
        Assert.Null(signal);
    }

    [Fact]
    public void SensitivePayloadFieldsDoNotEnterSignal()
    {
        const string Json = """
            {
              "hook_event_name": "UserPromptSubmit",
              "session_id": "session-1",
              "turn_id": "turn-1",
              "prompt": "private prompt",
              "last_assistant_message": "private answer",
              "transcript_path": "private path"
            }
            """;

        Assert.True(CodexActivityHookPayloadParser.TryParse(Json, out var signal));
        Assert.DoesNotContain("private", signal!.ToString(), StringComparison.Ordinal);
    }
}
