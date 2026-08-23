using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class OrganizeViewModel : ObservableObject, IBusyPage
{
    private readonly Func<OrganizeRequest, IProgress<int>?, CancellationToken, Task<OperationOutcome>> run;

    public OrganizeViewModel(Func<OrganizeRequest, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null)
    {
        this.run = run ?? ((request, progress, cancellationToken) =>
            QpdfOperationService.RunAsync(request, progress, cancellationToken));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string inputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string pageRanges = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string outputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private int? knownPageCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool isBusy;

    [ObservableProperty]
    private int statusPercent;

    [ObservableProperty]
    private OperationOutcome? outcome;

    public event EventHandler? BusyStateChanged;

    public bool RangeKnownBad =>
        KnownPageCount is int count && PageRanges.Trim().Length > 0 &&
        PageRangeParser.Parse(PageRanges, count) is null;

    public string RangeError
    {
        get
        {
            if (PageRanges.Trim().Length == 0)
            {
                return "Enter the pages to keep, in order — e.g. 1-3,5,z";
            }

            return RangeKnownBad
                ? $"Those pages don't exist — this PDF has {KnownPageCount} page(s)."
                : string.Empty;
        }
    }

    public OrganizeRequest CreateRequest(string qpdfPath) => new(qpdfPath, InputPath, PageRanges.Trim(), OutputPath);

    private bool CanRun()
    {
        if (IsBusy || InputPath.Trim().Length == 0 || OutputPath.Trim().Length == 0)
        {
            return false;
        }

        return PageRanges.Trim().Length > 0 && !RangeKnownBad;
    }

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

    partial void OnPageRangesChanged(string value) => OnPropertyChanged(nameof(RangeError));

    partial void OnKnownPageCountChanged(int? value) => OnPropertyChanged(nameof(RangeError));
}
