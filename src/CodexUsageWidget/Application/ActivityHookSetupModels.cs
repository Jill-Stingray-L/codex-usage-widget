namespace CodexUsageWidget.Application;

public enum ActivityHookSetupState
{
    NotInstalled,
    ApprovalRequired,
    Active,
    Modified,
    HooksDisabled,
    InstalledStatusUnavailable,
    Error
}

public enum ActivityHookChangeKind
{
    Install,
    Uninstall
}

public sealed record ActivityHookSetupStatus(
    ActivityHookSetupState State,
    string? Detail = null,
    bool HasInstalledHandlers = false);

public sealed record ActivityHookChangePreview(
    ActivityHookChangeKind Kind,
    bool HasChanges,
    string ProposedContent);
