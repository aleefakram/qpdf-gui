using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class CompressViewModel : ObservableObject, IBusyPage
{
    internal delegate Task<StagedCompressResult> StagedRun(
        StagedCompressInput input, IProgress<int>? progress, IProgress<bool>? isDownsampling,
        CancellationToken cancellationToken);

    private readonly StagedRun staged;

    // Legacy seam: a bare run func is wrapped as the runner's inner stage; the downsample service is used by default.
    public CompressViewModel(Func<CompressRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null)
        : this(new StagedCompressRunner(run).RunAsync)
    {
    }

    internal CompressViewModel(StagedRun staged)
    {
        ArgumentNullException.ThrowIfNull(staged);
        this.staged = staged;
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

    // JPEG quality for image re-encoding: 90 best, 75 balanced, 50 smallest.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private int imageQualityPercent = 75;

    // Maximum scan resolution in DPI. 0 means Original (no downsampling).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private int maxResolutionDpi;

    [ObservableProperty]
    private bool isDownsampling;

    [ObservableProperty]
    private string downsampleNote = string.Empty;

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

    public long InputBytes { get; set; }

    private bool CanRun() =>
        !IsBusy && InputPath.Trim().Length > 0 && OutputPath.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Outcome = null;
        StatusPercent = 0;
        DownsampleNote = string.Empty;
        IsBusy = true;
        try
        {
            var progress = new Progress<int>(value => StatusPercent = value);
            var phase = new Progress<bool>(value => IsDownsampling = value);
            var result = await staged(new StagedCompressInput(QpdfLocator.Path, InputPath, OutputPath,
                MaxResolutionDpi, ImageQualityPercent, LinearizeForWeb, AllowOverwrite),
                progress, phase, cancellationToken);
            Outcome = result.Outcome;
            DownsampleNote = result.Note;
        }
        finally
        {
            IsBusy = false;
            IsDownsampling = false;
            AllowOverwrite = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => BusyStateChanged?.Invoke(this, EventArgs.Empty);

    public void OnShellClosing() => RunCommand.Cancel();
}
