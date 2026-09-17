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
        string? temporaryOutputPath = null;

        try
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

            temporaryOutputPath = CreateTemporaryOutputPath(operation.OutputPath);

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
                var written = MoveOutputsToTarget(temporaryOutputPath, operation.OutputPath, allowOverwrite);
                progress?.Report(100);
                var outputBytes = written.Sum(path => new FileInfo(path).Length);
                return new OperationOutcome(true, result.ExitCode == 3, outputBytes, string.Empty, details);
            }

            return new OperationOutcome(false, false, null, FriendlyError(details), details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // Validation failures carry user-appropriate messages; surface them as outcomes,
        // never as exceptions — pages are async-void and must not crash the process.
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentOutOfRangeException)
        {
            return new OperationOutcome(false, false, null, exception.Message, exception.Message);
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return new OperationOutcome(false, false, null, FriendlyError(exception.Message), exception.Message);
        }
        finally
        {
            if (temporaryOutputPath is not null)
            {
                TryDeleteTempOutputs(temporaryOutputPath);
            }
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

    // Verified empirically: --split-pages=N appends "-<first>-<last>" to the ENTIRE
    // output path given (sp.pdf -> sp-1-2.pdf; ".x.abc.tmp" -> ".x.abc.tmp-1-2").
    // Single-output ops produce exactly the temp file. All shapes share the temp
    // filename as a prefix, which is how produced files are discovered below.
    private static bool TargetFilesExist(string temporaryOutputPath) =>
        TempOutputMatches(temporaryOutputPath).Any();

    // Returns the files written, so size reporting never counts unrelated look-alike names.
    private static IReadOnlyList<string> MoveOutputsToTarget(string temporaryOutputPath, string targetPath, bool allowOverwrite)
    {
        var matches = TempOutputMatches(temporaryOutputPath).ToList();
        if (matches.Count == 1 && Path.GetFullPath(matches[0]) == Path.GetFullPath(temporaryOutputPath))
        {
            File.Move(temporaryOutputPath, targetPath, overwrite: allowOverwrite);
            return [targetPath];
        }

        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        var targetStem = Path.GetFileNameWithoutExtension(targetPath);
        // Anchor at the FULL temp filename (including its .tmp extension): real qpdf
        // appends "-N-M" to the entire given path (".x.abc.tmp" -> ".x.abc.tmp-1-2"),
        // so the suffix is "-1-2" and destinations become "<targetStem>-1-2<targetExt>".
        var stemLength = Path.GetFileName(temporaryOutputPath).Length;
        var moves = new List<(string Source, string Destination)>(matches.Count);
        foreach (var produced in matches)
        {
            var fileName = Path.GetFileName(produced);
            var relative = fileName[stemLength..];
            var destination = Path.Combine(targetDirectory, targetStem + relative + Path.GetExtension(targetPath));
            if (!allowOverwrite && File.Exists(destination))
            {
                throw new IOException($"Refusing to overwrite existing split output: {destination}");
            }

            moves.Add((produced, destination));
        }

        // All-or-nothing: set replaced files aside first, and restore them if any move fails
        // (e.g. one output is open in a viewer), so a split never leaves a mixed set behind.
        var done = new List<(string Source, string Destination, string? Backup)>(moves.Count);
        try
        {
            foreach (var (source, destination) in moves)
            {
                string? backup = null;
                if (File.Exists(destination))
                {
                    backup = $"{destination}.{Guid.NewGuid():N}.bak";
                    File.Move(destination, backup);
                }

                done.Add((source, destination, backup));
                File.Move(source, destination);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            foreach (var (source, destination, backup) in Enumerable.Reverse(done))
            {
                if (File.Exists(destination) && !File.Exists(source))
                {
                    File.Move(destination, source);
                }

                if (backup is not null && File.Exists(backup))
                {
                    File.Move(backup, destination);
                }
            }

            throw;
        }

        foreach (var (_, _, backup) in done)
        {
            if (backup is not null)
            {
                try
                {
                    File.Delete(backup);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The split itself succeeded; a stray backup must not turn it into a failure.
                }
            }
        }

        return moves.Select(move => move.Destination).ToList();
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

    public static string FriendlyError(string details)
    {
        if (details.Contains("Refusing to overwrite existing split output", StringComparison.Ordinal) ||
            details.Contains("Cannot create a file when that file already exists", StringComparison.Ordinal))
        {
            return "Split files with those names already exist in that folder. " +
                   "Choose a different file-name start or folder, or delete the previous results.";
        }

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
