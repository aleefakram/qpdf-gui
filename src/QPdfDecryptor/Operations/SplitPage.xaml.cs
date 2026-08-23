using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QPdfDecryptor.Controls;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class SplitPage : UserControl, IBusyPage
{
    private readonly SplitViewModel viewModel = new();
    private int? cachedPageCount;

    public event EventHandler? BusyStateChanged;

    public bool IsBusy => viewModel.IsBusy;

    public SplitPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(SplitViewModel.IsBusy))
            {
                BusyStateChanged?.Invoke(this, EventArgs.Empty);
                RefreshChrome();
            }
            else if (eventArgs.PropertyName is nameof(SplitViewModel.Outcome) or nameof(SplitViewModel.StatusPercent))
            {
                RefreshChrome();
            }
            else if (eventArgs.PropertyName is nameof(SplitViewModel.InputPath) or nameof(SplitViewModel.PagesPerFile))
            {
                RefreshEstimateAsync();
            }
            else if (eventArgs.PropertyName is nameof(SplitViewModel.OutputStem))
            {
                RenderEstimate(cachedPageCount);
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
            Status.ShowRunning($"Splitting PDFs… {viewModel.StatusPercent}%");
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
                outcome.HasWarnings ? "Split with warnings" : "PDF split",
                $"Saved split files to {viewModel.OutputFolder}", outcome.Details);
        }
        else
        {
            Status.ShowFailure("Could not split", outcome.FriendlyError, outcome.Details);
        }
    }

    private async void RefreshEstimateAsync()
    {
        var inputPath = viewModel.InputPath;
        var pagesPerFile = viewModel.PagesPerFile;
        cachedPageCount = null;
        EstimateHint.Visibility = Visibility.Collapsed;
        if (inputPath.Trim().Length == 0 || pagesPerFile < 1)
        {
            return;
        }

        int? pageCount;
        try
        {
            pageCount = await PageCountService.GetPageCountAsync(
                QpdfLocator.Path, inputPath, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            pageCount = null;
        }

        // A newer input/pages-per-file change owns the hint now; drop this stale result.
        if (viewModel.InputPath != inputPath || viewModel.PagesPerFile != pagesPerFile)
        {
            return;
        }

        cachedPageCount = pageCount;
        RenderEstimate(pageCount);
    }

    private void RenderEstimate(int? pageCount)
    {
        EstimateHint.Visibility = Visibility.Visible;
        if (pageCount is not { } count)
        {
            EstimateHint.Text = "Could not read the page count.";
            return;
        }

        var files = (int)Math.Ceiling(count / (double)viewModel.PagesPerFile);
        var stem = viewModel.OutputStem.Trim();
        EstimateHint.Text = files == 1
            ? "Produces 1 file."
            : $"Produces about {files} files named {stem}-<first>-<last>.pdf " +
              $"(for example {stem}-1-{Math.Min(viewModel.PagesPerFile, count)}.pdf).";
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.RunCommand.CanExecute(null) || viewModel.IsBusy)
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
            viewModel.InputPath = dialog.FileName;
        }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(OwnerWindow()) == true)
        {
            viewModel.OutputFolder = dialog.FolderName;
        }
    }

    private void FewerPages_Click(object sender, RoutedEventArgs e) =>
        viewModel.PagesPerFile = Math.Clamp(viewModel.PagesPerFile - 1, 1, 9999);

    private void MorePages_Click(object sender, RoutedEventArgs e) =>
        viewModel.PagesPerFile = Math.Clamp(viewModel.PagesPerFile + 1, 1, 9999);

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
            viewModel.InputPath = files[0];
        }
    }
}
