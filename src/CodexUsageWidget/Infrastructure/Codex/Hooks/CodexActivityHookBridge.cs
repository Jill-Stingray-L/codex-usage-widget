namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public static class CodexActivityHookBridge
{
    private const int PipeConnectTimeoutMilliseconds = 1000;

    public const int HookTimeoutSeconds = 5;

    public static string Command => BuildCommand(CodexActivityPipeClient.DefaultPipeName);

    public static string BuildCommand(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        var escapedPipeName = pipeName.Replace("'", "''", StringComparison.Ordinal);

        return "$ErrorActionPreference='Stop';try{" +
               "$h=[Console]::In.ReadToEnd()|ConvertFrom-Json;" +
               "$k=@{'UserPromptSubmit'=0;'Stop'=1;'SessionEnd'=2}[[string]$h.hook_event_name];" +
               "$s=[string]$h.session_id;$t=[string]$h.turn_id;" +
               "if($null-ne$k-and-not[string]::IsNullOrWhiteSpace($s)-and" +
               "($k-eq2-or-not[string]::IsNullOrWhiteSpace($t))){" +
               "$u=$null;if($k-ne2){$u=$t};" +
               "$m=[ordered]@{Kind=$k;SessionId=$s;TurnId=$u}|ConvertTo-Json -Compress;" +
               "$b=[Text.Encoding]::UTF8.GetBytes($m);" +
               "$p=[IO.Pipes.NamedPipeClientStream]::new('.'," +
               $"'{escapedPipeName}',[IO.Pipes.PipeDirection]::Out," +
               "[IO.Pipes.PipeOptions]::Asynchronous);" +
               "try{" +
               $"$p.Connect({PipeConnectTimeoutMilliseconds});" +
               "$l=[BitConverter]::GetBytes([int]$b.Length);" +
               "$p.Write($l,0,$l.Length);$p.Write($b,0,$b.Length);$p.Flush()" +
               "}finally{$p.Dispose()}" +
               "}}catch{};[Console]::Out.WriteLine('{\"continue\":true}')";
    }
}
