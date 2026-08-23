using System.Windows;

namespace QPdfDecryptor.Controls;

public partial class OverwriteDialog : Window
{
    public enum Decision { Replace, ChooseDifferent, Dismissed }

    private Decision result = Decision.Dismissed;

    public OverwriteDialog() => InitializeComponent();

    public static Decision Ask(Window owner, string targetPath)
    {
        var dialog = new OverwriteDialog { Owner = owner };
        dialog.PromptTitle.Text = $"Replace {System.IO.Path.GetFileName(targetPath)}?";
        dialog.PromptBody.Text =
            "A file with this name already exists. Replacing it overwrites the existing copy. Your original files are never modified.";
        dialog.ShowDialog();
        return dialog.result;
    }

    private void Replace_Click(object sender, RoutedEventArgs e) { result = Decision.Replace; Close(); }
    private void DifferentName_Click(object sender, RoutedEventArgs e) { result = Decision.ChooseDifferent; Close(); }
}
