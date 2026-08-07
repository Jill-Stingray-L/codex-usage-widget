using System.Windows.Media;
using CodexUsageWidget.Application;

namespace CodexUsageWidget.Views.ViewModels;

public sealed class ActivityHookSetupViewModel
{
    private ActivityHookSetupViewModel()
    {
    }

    public string StatusLabel { get; private init; } = "Checking Codex…";

    public string Description { get; private init; } =
        "Reading the local hook configuration and Codex trust status.";

    public System.Windows.Media.Brush StatusBrush { get; private init; } =
        BrushFromHex("#D6A15F");

    public bool CanInstall { get; private init; }

    public bool CanOpenCodex { get; private init; }

    public bool CanUninstall { get; private init; }

    public bool CanRefresh { get; private init; }

    public string CodexActionLabel { get; private init; } = "Open in Codex";

    public static ActivityHookSetupViewModel Loading() => new();

    public static ActivityHookSetupViewModel FromStatus(ActivityHookSetupStatus status) =>
        status.State switch
        {
            ActivityHookSetupState.NotInstalled => new ActivityHookSetupViewModel
            {
                StatusLabel = "Setup required",
                Description =
                    "Install three local lifecycle hooks to animate the taskbar dots while Codex works. " +
                    "You can review the exact hooks.json change before it is written.",
                StatusBrush = BrushFromHex("#D6A15F"),
                CanInstall = true,
                CanRefresh = true
            },
            ActivityHookSetupState.ApprovalRequired => new ActivityHookSetupViewModel
            {
                StatusLabel = "One step left",
                Description =
                    "Hooks are installed. Approve their exact definitions once in Codex before they can run.",
                StatusBrush = BrushFromHex("#D6A15F"),
                CodexActionLabel = "Approve in Codex",
                CanOpenCodex = true,
                CanUninstall = true,
                CanRefresh = true
            },
            ActivityHookSetupState.Active => new ActivityHookSetupViewModel
            {
                StatusLabel = "Activity dots are ready",
                Description = "All three hooks are installed and trusted.",
                StatusBrush = BrushFromHex("#68B88A"),
                CanUninstall = true,
                CanRefresh = true
            },
            ActivityHookSetupState.Modified => new ActivityHookSetupViewModel
            {
                StatusLabel = "Review required",
                Description =
                    "Codex paused the changed definitions. Review them again before activity reporting resumes.",
                StatusBrush = BrushFromHex("#D6A15F"),
                CodexActionLabel = "Review in Codex",
                CanOpenCodex = true,
                CanUninstall = true,
                CanRefresh = true
            },
            ActivityHookSetupState.HooksDisabled => new ActivityHookSetupViewModel
            {
                StatusLabel = "Hooks are disabled in Codex",
                Description = status.Detail ??
                    "Set hooks = true in the [features] section of your Codex config before installing.",
                StatusBrush = BrushFromHex("#E16D76"),
                CanUninstall = status.HasInstalledHandlers,
                CanRefresh = true
            },
            ActivityHookSetupState.InstalledStatusUnavailable => new ActivityHookSetupViewModel
            {
                StatusLabel = "Installed · status unavailable",
                Description = status.Detail ??
                    "Hooks are installed, but their trust status could not be verified.",
                StatusBrush = BrushFromHex("#D6A15F"),
                CodexActionLabel = "Open hooks in Codex",
                CanOpenCodex = true,
                CanUninstall = true,
                CanRefresh = true
            },
            _ => new ActivityHookSetupViewModel
            {
                StatusLabel = "Setup unavailable",
                Description = status.Detail ?? "The activity hook configuration could not be read.",
                StatusBrush = BrushFromHex("#E16D76"),
                CanRefresh = true
            }
        };

    public static ActivityHookSetupViewModel Error(string message) => new()
    {
        StatusLabel = "Setup unavailable",
        Description = message,
        StatusBrush = BrushFromHex("#E16D76"),
        CanRefresh = true
    };

    private static SolidColorBrush BrushFromHex(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
