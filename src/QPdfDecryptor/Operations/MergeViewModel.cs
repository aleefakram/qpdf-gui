using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class MergeViewModel : ObservableObject, IBusyPage
{
    private readonly Func<MergeRequest, IProgress<int>?, CancellationToken, Task<OperationOutcome>> run;

    public MergeViewModel(Func<MergeRequest, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null)
    {
        this.run = run ?? ((request, progress, cancellationToken) =>
            QpdfOperationService.RunAsync(request, progress, cancellationToken));
        InputPaths.CollectionChanged += OnInputPathsChanged;
    }

    public ObservableCollection<string> InputPaths { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string outputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool isBusy;

    [ObservableProperty]
    private int statusPercent;

    [ObservableProperty]
    private OperationOutcome? outcome;

    public event EventHandler? BusyStateChanged;

    public MergeRequest CreateRequest(string qpdfPath) => new(qpdfPath, [.. InputPaths], OutputPath);

    private bool CanRun() => !IsBusy && InputPaths.Count > 0 && OutputPath.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Outcome = null;
        StatusPercent = 0;
        IsBusy = true;
        try
        {
            Outcome = await run(CreateRequest(QpdfLocator.Path),
                new Progress<int>(value => StatusPercent = value), cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => BusyStateChanged?.Invoke(this, EventArgs.Empty);

    private void OnInputPathsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RunCommand.NotifyCanExecuteChanged();
}
