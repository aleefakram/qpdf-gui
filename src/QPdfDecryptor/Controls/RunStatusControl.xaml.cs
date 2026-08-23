using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QPdfDecryptor.Controls;

public partial class RunStatusControl : UserControl
{
    public RunStatusControl() => InitializeComponent();

    public void ShowRunning(string message)
    {
        StatusRegion.Visibility = Visibility.Hidden;
        ProgressRegion.Visibility = Visibility.Visible;
        ProgressText.Text = message;
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
    }

    public void Report(int percent) => ProgressBar.Value = percent;

    public void ShowSuccess(bool warnings, string title, string message, string details)
    {
        SetStatus(warnings ? "\uE7BA" : "\uE73E",
                  (Brush)FindResource("WarningBrush"),
                  warnings ? (Brush)FindResource("WarningSurfaceBrush") : (Brush)FindResource("SuccessSurfaceBrush"));
        if (!warnings)
        {
            StatusIcon.Foreground = StatusTitle.Foreground = (Brush)FindResource("SuccessBrush");
        }

        StatusTitle.Text = title;
        StatusMessage.Text = message;
        SetDetails(details);
    }

    public void ShowFailure(string title, string message, string details)
    {
        SetStatus("\uE783", (Brush)FindResource("ErrorBrush"), (Brush)FindResource("ErrorSurfaceBrush"));
        StatusTitle.Text = title;
        StatusMessage.Text = message;
        SetDetails(details);
    }

    public void Clear()
    {
        ProgressRegion.Visibility = Visibility.Hidden;
        StatusRegion.Visibility = Visibility.Hidden;
    }

    private void SetStatus(string glyph, Brush foreground, Brush surface)
    {
        StatusRegion.Background = surface;
        StatusIcon.Text = glyph;
        StatusIcon.Foreground = foreground;
        StatusTitle.Foreground = foreground;
        StatusRegion.Visibility = Visibility.Visible;
    }

    private void SetDetails(string details)
    {
        DetailsText.Text = details;
        DetailsExpander.Visibility = string.IsNullOrEmpty(details) ? Visibility.Collapsed : Visibility.Visible;
        DetailsExpander.IsExpanded = false;
    }
}
