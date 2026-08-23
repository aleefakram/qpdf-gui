using System.Diagnostics;
using System.Text.RegularExpressions;

namespace QPdfDecryptor.Core;

public sealed record OperationOutcome(bool Succeeded, bool HasWarnings, long? OutputBytes, string FriendlyError, string Details);

public static partial class QpdfOperationService
{
    // Shared pipeline for every file-producing operation: validate, spawn qpdf against a
    // temporary output, report progress, then move onto the real target only on success.
    public static async Task<OperationOutcome> RunAsync(
        IQpdfFileOperation operation,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowOverwrite = false)
    {
        operation.Validate();

        if (operation.ProbeInputPath.Length > 0)
        {
            var probe = await QpdfPasswordProbe.ProbeAsync(
                operation.QpdfPath, operation.ProbeInputPath, string.Empty, cancellationToken);
            if (probe.Outcome == ProbeOutcome.Wrong)
            {
                return new OperationOutcome(false, false, null,
                    "This PDF is password-protected. Use Decrypt first.", probe.Error);
            }
        }

        var temporaryOutputPath = CreateTemporaryOutputPath(operation.OutputPath);

        try
        {
            var startInfo = QpdfProcessRunner.CreateStartInfo(operation.QpdfPath);
            foreach (var argument in operation.BuildArguments(temporaryOutputPath))
            {
                startInfo.ArgumentList.Add(argument);
            }

            var result = await QpdfProcessRunner.RunAsync(
                startInfo, stdinText: null, outputLine: line => ReportProgress(line, progress), cancellationToken);

            var details = string.Join(Environment.NewLine, new[] { result.Error.Trim(), result.Output.Trim() }
                .Where(value => value.Length > 0));
            if ((result.ExitCode is 0 or 3) && TargetFilesExist(temporaryOutputPath))
            {
                MoveOutputsToTarget(temporaryOutputPath, operation.OutputPath, allowOverwrite);
                progress?.Report(100);
                return new OperationOutcome(true, result.ExitCode == 3, OutputSize(operation.OutputPath), string.Empty, details);
            }

            return new OperationOutcome(false, false, null, FriendlyError(details), details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return new OperationOutcome(false, false, null, "The PDF could not be processed.", exception.Message);
        }
        finally
        {
            TryDeleteTempOutputs(temporaryOutputPath);
        }

        static void ReportProgress(string line, IProgress<int>? sink)
        {
            var match = ProgressPattern().Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
            {
                sink?.Report(Math.Clamp(value, 0, 100));
            }
        }
    }

    // Verified empirically: --split-pages=N names outputs <stem>-<first>-<last>.pdf as
    // SIBLINGS of the given output path (e.g. sp.pdf -> sp-1-2.pdf). Single-output ops
    // produce exactly the temp file. Both shapes share the temp filename's stem prefix.
    private static bool TargetFilesExist(string temporaryOutputPath) =>
        TempOutputMatches(temporaryOutputPath).Any();

    private static void MoveOutputsToTarget(string temporaryOutputPath, string targetPath, bool allowOverwrite)
    {
        var matches = TempOutputMatches(temporaryOutputPath).ToList();
        if (matches.Count == 1 && Path.GetFullPath(matches[0]) == Path.GetFullPath(temporaryOutputPath))
        {
            File.Move(temporaryOutputPath, targetPath, overwrite: allowOverwrite);
            return;
        }

        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        var targetStem = Path.GetFileNameWithoutExtension(targetPath);
        var stemLength = Path.GetFileNameWithoutExtension(temporaryOutputPath).Length;
        var moves = new List<(string Source, string Destination)>(matches.Count);
        foreach (var produced in matches)
        {
            var fileName = Path.GetFileName(produced);
            var relative = Path.GetFileNameWithoutExtension(fileName[stemLength..]);
            var destination = Path.Combine(targetDirectory, targetStem + relative + Path.GetExtension(targetPath));
            if (!allowOverwrite && File.Exists(destination))
            {
                throw new IOException($"Refusing to overwrite existing split output: {destination}");
            }

            moves.Add((produced, destination));
        }

        foreach (var (source, destination) in moves)
        {
            File.Move(source, destination, overwrite: allowOverwrite);
        }
    }

    private static IEnumerable<string> TempOutputMatches(string temporaryOutputPath)
    {
        var fullTempPath = Path.GetFullPath(temporaryOutputPath);
        var directory = Path.GetDirectoryName(fullTempPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullTempPath);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, stem + "*")
            : [];
    }

    private static long? OutputSize(string targetPath)
    {
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (File.Exists(fullTargetPath))
        {
            return new FileInfo(fullTargetPath).Length;
        }

        // Split runs never produce the exact target file, only <stem><suffix> siblings.
        var directory = Path.GetDirectoryName(fullTargetPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullTargetPath);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        long total = 0;
        foreach (var produced in Directory.EnumerateFiles(directory, stem + "*"))
        {
            total += new FileInfo(produced).Length;
        }

        return total > 0 ? total : null;
    }

    public static string FriendlyError(string details)
    {
        if (details.Contains("invalid password", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("password is incorrect", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("input file is encrypted", StringComparison.OrdinalIgnoreCase))
        {
            return "This PDF is password-protected. Use Decrypt first.";
        }

        if (details.Contains("invalid page range", StringComparison.OrdinalIgnoreCase))
        {
            return "Those pages don't exist in this PDF. Check the range and try again.";
        }

        if (details.Contains("not a PDF file", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("can't find PDF header", StringComparison.OrdinalIgnoreCase))
        {
            return "That file is not a valid PDF.";
        }

        if (details.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("used by another process", StringComparison.OrdinalIgnoreCase))
        {
            return "The output file cannot be written. Close it if it is open, or choose another location.";
        }

        return "The PDF could not be processed.";
    }

    internal static string CreateTemporaryOutputPath(string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new ArgumentException("The output path must include a directory.", nameof(outputPath));
        return Path.Combine(directory, $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteTempOutputs(string temporaryOutputPath)
    {
        foreach (var leftover in TempOutputMatches(temporaryOutputPath))
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // One undeletable leftover must not abandon the remaining cleanup.
            }
        }
    }

    [GeneratedRegex(@"write progress:\s*(\d+)%", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressPattern();
}
