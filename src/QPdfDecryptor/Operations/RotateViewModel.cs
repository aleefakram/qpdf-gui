using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class RotateViewModel : ObservableObject, IBusyPage
{
    private readonly Func<RotateRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>> run;

    public RotateViewModel(Func<RotateRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null)
    {
        this.run = run ?? ((request, allowOverwrite, progress, cancellationToken) =>
            QpdfOperationService.RunAsync(request, progress, cancellationToken, allowOverwrite));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string inputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string outputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string pageRange = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private int selectedAngle = 90;

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

    [ObservableProperty]
    private bool allowOverwrite;

    public event EventHandler? BusyStateChanged;

    public bool RangeKnownBad =>
        KnownPageCount is int count && PageRange.Trim().Length > 0 &&
        PageRangeParser.Parse(PageRange, count) is null;

    public string RangeError => RangeKnownBad
        ? $"Those pages don't exist — this PDF has {KnownPageCount} page(s)."
        : string.Empty;

    public RotateRequest CreateRequest(string qpdfPath) =>
        new(qpdfPath, InputPath, SelectedAngle,
            string.IsNullOrWhiteSpace(PageRange) ? null : PageRange.Trim(), OutputPath);

    private bool CanRun()
    {
        if (IsBusy || InputPath.Trim().Length == 0 || OutputPath.Trim().Length == 0)
        {
            return false;
        }

        // Grammar gate works without a page count (see OrganizeViewModel).
        if (PageRange.Trim().Length > 0 && !PageRangeParser.IsValid(PageRange, int.MaxValue))
        {
            return false;
        }

        return !RangeKnownBad;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Outcome = null;
        StatusPercent = 0;
        IsBusy = true;
        try
        {
            Outcome = await run(CreateRequest(QpdfLocator.Path), AllowOverwrite,
                new Progress<int>(value => StatusPercent = value), cancellationToken);
        }
        finally
        {
            IsBusy = false;
            AllowOverwrite = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => BusyStateChanged?.Invoke(this, EventArgs.Empty);

    public void OnShellClosing() => RunCommand.Cancel();

    partial void OnPageRangeChanged(string value) => OnPropertyChanged(nameof(RangeError));

    partial void OnKnownPageCountChanged(int? value) => OnPropertyChanged(nameof(RangeError));
}
