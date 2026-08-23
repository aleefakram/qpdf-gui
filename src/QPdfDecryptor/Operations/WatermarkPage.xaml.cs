using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QPdfDecryptor.Controls;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class WatermarkPage : UserControl, IBusyPage
{
    private readonly WatermarkViewModel viewModel = new();

    public event EventHandler? BusyStateChanged;

    public bool IsBusy => viewModel.IsBusy;

    public WatermarkPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(WatermarkViewModel.IsBusy))
            {
                BusyStateChanged?.Invoke(this, EventArgs.Empty);
                RefreshChrome();
            }
            else if (eventArgs.PropertyName is nameof(WatermarkViewModel.Outcome) or nameof(WatermarkViewModel.StatusPercent))
            {
                RefreshChrome();
            }
        };
        PreviewKeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Enter && viewModel.RunCommand.CanExecute(null))
            {
                Run_Click(this, new RoutedEventArgs());
                eventArgs.Handled = true;
            }
        };
    }

    private void RefreshChrome()
    {
        CancelButton.Visibility = viewModel.IsBusy ? Visibility.Visible : Visibility.Hidden;
        if (viewModel.IsBusy)
        {
            Status.ShowRunning($"Applying watermark… {viewModel.StatusPercent}%");
            Status.Report(viewModel.StatusPercent);
            return;
        }

        if (viewModel.Outcome is not { } outcome)
        {
            return;
        }

        if (outcome.Succeeded)
        {
            Status.ShowSuccess(outcome.HasWarnings,
                outcome.HasWarnings ? "Watermarked with warnings" : "PDF watermarked",
                $"Saved {viewModel.OutputPath}", outcome.Details);
        }
        else
        {
            Status.ShowFailure("Could not watermark", outcome.FriendlyError, outcome.Details);
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.RunCommand.CanExecute(null) || viewModel.IsBusy)
        {
            return;
        }

        if (System.IO.File.Exists(viewModel.OutputPath) &&
            OverwriteDialog.Ask(OwnerWindow(), viewModel.OutputPath) != OverwriteDialog.Decision.Replace)
        {
            return;
        }

        Status.Clear();
        try
        {
            await viewModel.RunCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            Status.Clear();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => viewModel.RunCommand.Cancel();

    private Window OwnerWindow() => Window.GetWindow(this)!;

    private void ChoosePdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = false, Filter = "PDF files|*.pdf" };
        if (dialog.ShowDialog(OwnerWindow()) == true)
        {
            SetInputPath(dialog.FileName);
        }
    }

    private void ChooseWatermark_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = false, Filter = "PDF files|*.pdf" };
        if (dialog.ShowDialog(OwnerWindow()) == true)
        {
            viewModel.WatermarkPdfPath = dialog.FileName;
        }
    }

    private void Placement_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            viewModel.BehindContent = tag == "behind";
        }
    }

    private void SetInputPath(string path) => viewModel.InputPath = path;

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(viewModel.InputPath);
        var dialog = new SaveFileDialog
        {
            Filter = "PDF files|*.pdf",
            FileName = string.IsNullOrEmpty(stem) ? "watermarked.pdf" : $"{stem}-watermarked.pdf",
        };
        if (dialog.ShowDialog(OwnerWindow()) == true)
        {
            viewModel.OutputPath = dialog.FileName;
        }
    }

    private void DropTarget_DragEnter(object sender, DragEventArgs eventArgs) =>
        eventArgs.Effects = eventArgs.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private void DropTarget_Drop(object sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || viewModel.IsBusy)
        {
            return;
        }

        if (files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            SetInputPath(files[0]);
        }
    }
}
