namespace CodexUsageWidget.Views.Controls;

public partial class DetailedUsageView : System.Windows.Controls.UserControl
{
    public DetailedUsageView()
    {
        InitializeComponent();
    }

    public void ScrollToTop() => Dispatcher.BeginInvoke(
        () =>
        {
            UpdateLayout();
            DetailsScrollViewer.ScrollToHome();
        },
        System.Windows.Threading.DispatcherPriority.ContextIdle);
}
