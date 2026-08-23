namespace QPdfDecryptor.Core;

public static class PageCountService
{
    // Runs "qpdf --show-npages <file>" and returns the page count, or null on failure.
    public static async Task<int?> GetPageCountAsync(
        string qpdfPath, string pdfPath, CancellationToken cancellationToken)
    {
        var startInfo = QpdfProcessRunner.CreateStartInfo(qpdfPath);
        startInfo.ArgumentList.Add("--show-npages");
        startInfo.ArgumentList.Add(pdfPath);

        var result = await QpdfProcessRunner.RunAsync(startInfo, stdinText: null, outputLine: null, cancellationToken);
        return result.ExitCode == 0 && int.TryParse(result.Output.Trim(), out var pages)
            ? pages
            : null;
    }
}
