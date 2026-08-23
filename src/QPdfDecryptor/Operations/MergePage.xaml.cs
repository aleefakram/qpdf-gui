using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QPdfDecryptor.Controls;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class MergePage : UserControl, IBusyPage
{
    private readonly MergeViewModel viewModel = new();
    private Point dragOrigin;

    public event EventHandler? BusyStateChanged;

    public bool IsBusy => viewModel.IsBusy;

    public MergePage()
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(MergeViewModel.IsBusy))
            {
                BusyStateChanged?.Invoke(this, EventArgs.Empty);
                RefreshChrome();
            }
            else if (eventArgs.PropertyName is nameof(MergeViewModel.Outcome) or nameof(MergeViewModel.StatusPercent))
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
            Status.ShowRunning($"Merging PDFs… {viewModel.StatusPercent}%");
            Status.Report(viewModel.StatusPercent);
            return;
        }

        if (viewModel.Outcome is not { } outcome)
        {
            return;
        }

        if (outcome.Succeeded)
        {
            var message = outcome.OutputBytes is { } bytes
                ? $"Saved {viewModel.OutputPath} ({bytes / 1024d / 1024d:N1} MB)"
                : $"Saved {viewModel.OutputPath}";
            Status.ShowSuccess(outcome.HasWarnings,
                outcome.HasWarnings ? "Merged with warnings" : "PDF merged", message, outcome.Details);
        }
        else
        {
            Status.ShowFailure("Could not merge", outcome.FriendlyError, outcome.Details);
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

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "PDF files|*.pdf" };
        if (dialog.ShowDialog(OwnerWindow()) == true)
        {
            foreach (var file in dialog.FileNames)
            {
                if (!viewModel.InputPaths.Contains(file))
                {
                    viewModel.InputPaths.Add(file);
                }
            }
        }
    }

    private void RemoveFile_Click(object sender, RoutedEventArgs e) =>
        viewModel.InputPaths.Remove((string)((Button)sender).DataContext);

    private void DropTarget_DragEnter(object sender, DragEventArgs eventArgs) =>
        eventArgs.Effects = eventArgs.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private void DropTarget_Drop(object sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data.GetData(DataFormats.FileDrop) is not string[] files || viewModel.IsBusy)
        {
            return;
        }

        foreach (var file in files.Where(IsPdf))
        {
            if (!viewModel.InputPaths.Contains(file))
            {
                viewModel.InputPaths.Add(file);
            }
        }
    }

    private static bool IsPdf(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PDF files|*.pdf", FileName = "merged.pdf" };
        if (dialog.ShowDialog(OwnerWindow()) == true)
        {
            viewModel.OutputPath = dialog.FileName;
        }
    }

    private void FileList_MouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (viewModel.IsBusy || eventArgs.LeftButton != MouseButtonState.Pressed) return;
        if (ItemDataContextAt(eventArgs.OriginalSource as DependencyObject) is not string fileName) return;
        var now = eventArgs.GetPosition(FileList);
        if (now == dragOrigin) return;
        dragOrigin = now;
        DragDrop.DoDragDrop(FileList, fileName, DragDropEffects.Move);
    }

    private void FileList_Drop(object sender, DragEventArgs eventArgs)
    {
        if (viewModel.IsBusy || eventArgs.Data.GetData(DataFormats.StringFormat) is not string fileName) return;
        var originalIndex = viewModel.InputPaths.IndexOf(fileName);
        if (originalIndex < 0) return;
        var insertAt = InsertIndexFrom(eventArgs.GetPosition(FileList));
        viewModel.InputPaths.RemoveAt(originalIndex);
        if (insertAt > originalIndex) insertAt--;
        viewModel.InputPaths.Insert(Math.Clamp(insertAt, 0, viewModel.InputPaths.Count), fileName);
    }

    private int InsertIndexFrom(Point position)
    {
        for (var index = 0; index < FileList.Items.Count; index++)
        {
            if (FileList.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter presenter &&
                position.Y < presenter.TranslatePoint(new Point(0, presenter.ActualHeight / 2), FileList).Y)
            {
                return index;
            }
        }

        return FileList.Items.Count;
    }

    private string? ItemDataContextAt(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ContentPresenter { DataContext: string fileName })
            {
                return fileName;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
