using CodexUsageWidget.Application;

namespace CodexUsageWidget.Infrastructure.Codex.Hooks;

public sealed class CodexActivityHookSetupService : IActivityHookSetupService
{
    private readonly CodexHookConfigurationManager _configurationManager;
    private readonly ICodexAppServerSession _session;
    private readonly string _processPath;
    private readonly string _workingDirectory;

    public CodexActivityHookSetupService(
        CodexHookConfigurationManager configurationManager,
        ICodexAppServerSession session,
        string processPath,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentNullException.ThrowIfNull(configurationManager);
        ArgumentNullException.ThrowIfNull(session);
        _configurationManager = configurationManager;
        _session = session;
        _processPath = processPath;
        _workingDirectory = workingDirectory ?? Environment.CurrentDirectory;
    }

    public async Task<ActivityHookSetupStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var configurationPlan = _configurationManager.PlanInstall(_processPath);
        if (configurationPlan.Error is not null)
        {
            return new ActivityHookSetupStatus(
                configurationPlan.ErrorKind == CodexHookConfigurationErrorKind.HooksDisabled
                    ? ActivityHookSetupState.HooksDisabled
                    : ActivityHookSetupState.Error,
                configurationPlan.Error);
        }

        if (configurationPlan.HasChanges)
        {
            return new ActivityHookSetupStatus(ActivityHookSetupState.NotInstalled);
        }

        try
        {
            var result = await _session.RequestAsync(
                    "hooks/list",
                    new { cwds = new[] { _workingDirectory } },
                    cancellationToken)
                .ConfigureAwait(false);
            return FromTrustEvaluation(CodexHookTrustStatusParser.Parse(
                result,
                CodexHookConfigurationManager.BuildHookCommand(_processPath)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ActivityHookSetupStatus(
                ActivityHookSetupState.InstalledStatusUnavailable,
                $"Codex could not report hook trust status: {ex.Message}");
        }
    }

    public ActivityHookChangePreview PrepareChange(ActivityHookChangeKind kind)
    {
        var plan = CreatePlan(kind);
        EnsureValid(plan);
        return new ActivityHookChangePreview(kind, plan.HasChanges, plan.ProposedContent);
    }

    public void ApplyChange(ActivityHookChangePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var currentPlan = CreatePlan(preview.Kind);
        EnsureValid(currentPlan);

        if (currentPlan.HasChanges != preview.HasChanges ||
            !string.Equals(
                currentPlan.ProposedContent,
                preview.ProposedContent,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Codex hooks changed after the preview. Review the updated change and try again.");
        }

        _configurationManager.Apply(currentPlan);
    }

    private CodexHookConfigurationPlan CreatePlan(ActivityHookChangeKind kind) =>
        kind == ActivityHookChangeKind.Install
            ? _configurationManager.PlanInstall(_processPath)
            : _configurationManager.PlanUninstall(_processPath);

    private static void EnsureValid(CodexHookConfigurationPlan plan)
    {
        if (plan.Error is not null)
        {
            throw new InvalidOperationException(plan.Error);
        }
    }

    private static ActivityHookSetupStatus FromTrustEvaluation(
        CodexHookTrustEvaluation evaluation) =>
        evaluation switch
        {
            CodexHookTrustEvaluation.ApprovalRequired =>
                new ActivityHookSetupStatus(ActivityHookSetupState.ApprovalRequired),
            CodexHookTrustEvaluation.Active =>
                new ActivityHookSetupStatus(ActivityHookSetupState.Active),
            CodexHookTrustEvaluation.Modified =>
                new ActivityHookSetupStatus(ActivityHookSetupState.Modified),
            _ => new ActivityHookSetupStatus(
                ActivityHookSetupState.InstalledStatusUnavailable,
                "The hook definitions are installed, but Codex did not report all expected hooks.")
        };
}
