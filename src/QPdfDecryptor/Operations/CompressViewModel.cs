using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

public partial class CompressViewModel : ObservableObject, IBusyPage
{
    internal delegate Task<DownsampleResult> DownsampleStep(string qpdfPath, string inputPath,
        int maxDpi, int jpegQuality, IProgress<int>? progress, CancellationToken cancellationToken);

    private readonly Func<CompressRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>> run;
    private readonly DownsampleStep downsample;

    public CompressViewModel(Func<CompressRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null)
        : this(run, null)
    {
    }

    internal CompressViewModel(Func<CompressRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run,
        DownsampleStep? downsample)
    {
        this.run = run ?? ((request, allowOverwrite, progress, cancellationToken) =>
            QpdfOperationService.RunAsync(request, progress, cancellationToken, allowOverwrite));
        this.downsample = downsample ?? ScanDownsampleService.DownsampleAsync;
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

    public CompressRequest CreateRequest(string qpdfPath) =>
        new(qpdfPath, InputPath, LinearizeForWeb, OutputPath, ImageQualityPercent);

    private bool CanRun() =>
        !IsBusy && InputPath.Trim().Length > 0 && OutputPath.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Outcome = null;
        StatusPercent = 0;
        IsBusy = true;
        string? downsampled = null;
        try
        {
            var inputPath = InputPath;
            if (MaxResolutionDpi > 0)
            {
                IsDownsampling = true;
                try
                {
                    var progress = new Progress<int>(value => StatusPercent = value);
                    var step = await downsample(QpdfLocator.Path, InputPath,
                        MaxResolutionDpi, ImageQualityPercent, progress, cancellationToken);
                    if (step.Applied && step.QdfPath is not null)
                    {
                        inputPath = step.QdfPath;
                        downsampled = step.QdfPath;
                    }
                }
                finally
                {
                    IsDownsampling = false;
                    StatusPercent = 0;
                }
            }

            Outcome = await run(new CompressRequest(QpdfLocator.Path, inputPath,
                LinearizeForWeb, OutputPath, ImageQualityPercent), AllowOverwrite,
                new Progress<int>(value => StatusPercent = value), cancellationToken);
        }
        finally
        {
            IsBusy = false;
            AllowOverwrite = false;
            if (downsampled is not null)
            {
                ScanDownsampleService.DeleteWorkspaceFor(downsampled);
            }
        }
    }

    partial void OnIsBusyChanged(bool value) => BusyStateChanged?.Invoke(this, EventArgs.Empty);

    public void OnShellClosing() => RunCommand.Cancel();
}
