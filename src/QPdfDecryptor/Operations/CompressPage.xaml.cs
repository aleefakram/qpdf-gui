using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QPdfDecryptor.Controls;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class CompressPage : UserControl, IBusyPage
{
    private const double BytesPerMegabyte = 1024d * 1024d;

    private readonly CompressViewModel viewModel = new();

    public event EventHandler? BusyStateChanged;

    public bool IsBusy => viewModel.IsBusy;

    public CompressPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(CompressViewModel.IsBusy))
            {
                BusyStateChanged?.Invoke(this, EventArgs.Empty);
                RefreshChrome();
            }
            else if (eventArgs.PropertyName is nameof(CompressViewModel.Outcome) or nameof(CompressViewModel.StatusPercent))
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
            Status.ShowRunning($"Compressing PDF… {viewModel.StatusPercent}%");
            Status.Report(viewModel.StatusPercent);
            return;
        }

        if (viewModel.Outcome is not { } outcome)
        {
            return;
        }

        if (outcome.Succeeded)
        {
            var savedMessage = $"Saved {viewModel.OutputPath}";
            if (viewModel.InputBytes > 0 && outcome.OutputBytes is long outputBytes)
            {
                var beforeMb = viewModel.InputBytes / BytesPerMegabyte;
                var afterMb = outputBytes / BytesPerMegabyte;
                savedMessage = $"Saved {viewModel.OutputPath} ({beforeMb:N1} MB \u2192 {afterMb:N1} MB)";
            }

            Status.ShowSuccess(outcome.HasWarnings,
                outcome.HasWarnings ? "Compressed with warnings" : "PDF compressed",
                savedMessage, outcome.Details);
        }
        else
        {
            Status.ShowFailure("Could not compress", outcome.FriendlyError, outcome.Details);
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.RunCommand.CanExecute(null) || viewModel.IsBusy)
        {
            return;
        }

        if (System.IO.File.Exists(viewModel.OutputPath))
        {
            var decision = OverwriteDialog.Ask(OwnerWindow(), viewModel.OutputPath);
            if (decision == OverwriteDialog.Decision.Dismissed)
            {
                return;
            }

            if (decision == OverwriteDialog.Decision.ChooseDifferent)
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "PDF files|*.pdf",
                    FileName = System.IO.Path.GetFileName(viewModel.OutputPath),
                };
                if (dialog.ShowDialog(OwnerWindow()) != true)
                {
                    return;
                }

                viewModel.OutputPath = dialog.FileName;
            }
            else
            {
                viewModel.AllowOverwrite = true;
            }
        }

        Status.Clear();
        try
        {
            viewModel.InputBytes = System.IO.File.Exists(viewModel.InputPath)
                ? new System.IO.FileInfo(viewModel.InputPath).Length
                : 0;
            await viewModel.RunCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            Status.Clear();
        }
        finally
        {
            viewModel.AllowOverwrite = false;
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

    private void SetInputPath(string path) => viewModel.InputPath = path;

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(viewModel.InputPath);
        var dialog = new SaveFileDialog
        {
            Filter = "PDF files|*.pdf",
            FileName = string.IsNullOrEmpty(stem) ? "compressed.pdf" : $"{stem}-compressed.pdf",
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
