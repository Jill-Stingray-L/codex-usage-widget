using System.Windows;
using System.Windows.Input;
using CodexUsageWidget.Application;

namespace CodexUsageWidget.Views;

public partial class ActivityHookChangeReviewWindow : Window
{
    public ActivityHookChangeReviewWindow(ActivityHookChangePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        InitializeComponent();

        var installing = preview.Kind == ActivityHookChangeKind.Install;
        HeadingText.Text = installing
            ? "Review activity hook installation"
            : "Review activity hook removal";
        DescriptionText.Text = installing
            ? "The widget will write this exact ~/.codex/hooks.json content. Existing hooks and unknown fields are preserved."
            : "Only handlers that exactly match this widget executable will be removed. Other hook definitions are preserved.";
        ProposedContentText.Text = preview.ProposedContent;
        ApplyButton.Content = installing ? "Install hooks" : "Remove hooks";
        if (!installing)
        {
            ApplyButton.Style = (Style)FindResource("DangerDialogButton");
        }
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
