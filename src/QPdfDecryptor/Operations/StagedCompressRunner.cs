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
        var outcome = await run(new CompressRequest(input.QpdfPath, input.InputPath,
            input.LinearizeForWeb, input.OutputPath, input.JpegQuality), input.AllowOverwrite,
            progress, cancellationToken);
        return new StagedCompressResult(outcome, string.Empty);
    }
}
