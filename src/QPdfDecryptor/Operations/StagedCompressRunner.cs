using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

internal sealed record StagedCompressInput(
    string QpdfPath,
    string InputPath,
    string OutputPath,
    int MaxResolutionDpi,
    int JpegQuality,
    bool LinearizeForWeb,
    bool AllowOverwrite);

internal sealed record StagedCompressResult(OperationOutcome Outcome, string Note);

internal sealed class StagedCompressRunner
{
    internal delegate Task<DownsampleResult> DownsampleStep(string qpdfPath, string inputPath,
        int maxDpi, int jpegQuality, IProgress<int>? progress, CancellationToken cancellationToken);

    private readonly Func<CompressRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>> run;
    private readonly DownsampleStep downsample;

    public StagedCompressRunner(
        Func<CompressRequest, bool, IProgress<int>?, CancellationToken, Task<OperationOutcome>>? run = null,
        DownsampleStep? downsample = null)
    {
        this.run = run ?? ((request, allowOverwrite, progress, cancellationToken) =>
            QpdfOperationService.RunAsync(request, progress, cancellationToken, allowOverwrite));
        this.downsample = downsample ?? ScanDownsampleService.DownsampleAsync;
    }

    public async Task<StagedCompressResult> RunAsync(
        StagedCompressInput input,
        IProgress<int>? progress,
        IProgress<bool>? isDownsampling,
        CancellationToken cancellationToken)
    {
        string? downsampled = null;
        string note = string.Empty;
        try
        {
            var effectiveInput = input.InputPath;
            if (input.MaxResolutionDpi > 0)
            {
                isDownsampling?.Report(true);
                try
                {
                    var step = await downsample(input.QpdfPath, input.InputPath,
                        input.MaxResolutionDpi, input.JpegQuality, progress, cancellationToken);
                    if (step.Applied && step.QdfPath is not null)
                    {
                        effectiveInput = step.QdfPath;
                        downsampled = step.QdfPath;
                        note = $"Downsampled {step.ImagesDownsampled} scan " +
                            $"image{(step.ImagesDownsampled == 1 ? string.Empty : "s")} to {input.MaxResolutionDpi} DPI first.";
                    }
                    else if (step.ImagesConsidered > 0)
                    {
                        note = "Scans found but already at or below " +
                            $"{input.MaxResolutionDpi} DPI — compressed without downsampling.";
                    }
                    else
                    {
                        note = "No large scans found — compressed without downsampling.";
                    }
                }
                finally
                {
                    isDownsampling?.Report(false);
                    progress?.Report(0);
                }
            }

            var outcome = await run(new CompressRequest(input.QpdfPath, effectiveInput,
                input.LinearizeForWeb, input.OutputPath, input.JpegQuality), input.AllowOverwrite,
                progress, cancellationToken);
            return new StagedCompressResult(outcome, note);
        }
        finally
        {
            if (downsampled is not null)
            {
                ScanDownsampleService.DeleteWorkspaceFor(downsampled);
            }
        }
    }
}
