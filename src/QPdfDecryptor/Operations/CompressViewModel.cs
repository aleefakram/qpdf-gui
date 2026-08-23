using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class CompressViewModel : ObservableObject, IBusyPage
{
    private readonly Func<CompressRequest, IProgress<int>?, CancellationToken, Task<OperationOutcome>> run;

    public CompressViewModel(Func<CompressRequest, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null)
    {
        this.run = run ?? ((request, progress, cancellationToken) =>
            QpdfOperationService.RunAsync(request, progress, cancellationToken));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string inputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string outputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool linearizeForWeb = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool isBusy;

    [ObservableProperty]
    private int statusPercent;

    [ObservableProperty]
    private OperationOutcome? outcome;

    public event EventHandler? BusyStateChanged;

    public long InputBytes { get; set; }

    public CompressRequest CreateRequest(string qpdfPath) =>
        new(qpdfPath, InputPath, LinearizeForWeb, OutputPath);

    private bool CanRun() =>
        !IsBusy && InputPath.Trim().Length > 0 && OutputPath.Trim().Length > 0;

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
}
