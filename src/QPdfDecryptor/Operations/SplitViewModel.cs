using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class SplitViewModel : ObservableObject, IBusyPage
{
    private readonly Func<SplitRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>> run;

    public SplitViewModel(Func<SplitRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null)
    {
        this.run = run ?? ((request, allowOverwrite, progress, cancellationToken) =>
            QpdfOperationService.RunAsync(request, progress, cancellationToken, allowOverwrite));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string inputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private int pagesPerFile = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string outputFolder = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string outputStem = string.Empty;

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

    public SplitRequest CreateRequest(string qpdfPath) =>
        new(qpdfPath, InputPath, PagesPerFile,
            System.IO.Path.Combine(OutputFolder.Trim(), OutputStem.Trim() + ".pdf"));

    private bool CanRun() =>
        !IsBusy && InputPath.Trim().Length > 0 && PagesPerFile >= 1 &&
        OutputFolder.Trim().Length > 0 && OutputStem.Trim().Length > 0;

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

    public string PagesUnit => PagesPerFile == 1 ? "page" : "pages";

    partial void OnPagesPerFileChanged(int value) => OnPropertyChanged(nameof(PagesUnit));

    public void OnShellClosing() => RunCommand.Cancel();
}
