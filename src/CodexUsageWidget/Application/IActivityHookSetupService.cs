namespace CodexUsageWidget.Application;

public interface IActivityHookSetupService
{
    Task<ActivityHookSetupStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    ActivityHookChangePreview PrepareChange(ActivityHookChangeKind kind);

    void ApplyChange(ActivityHookChangePreview preview);
}
