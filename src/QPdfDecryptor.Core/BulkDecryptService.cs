namespace QPdfDecryptor.Core;

public sealed class BulkDecryptService
{
    private readonly QpdfDecryptService decryptService = new();

    public async Task<BulkDecryptResult> DecryptAsync(
        BulkDecryptRequest request,
        IProgress<BulkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var candidates = new List<string>(request.PasswordCandidates);
        var results = new List<FileResult>(request.InputPaths.Count);
        for (var index = 0; index < request.InputPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await DecryptOneAsync(
                request,
                request.InputPaths[index],
                candidates,
                progress,
                index,
                cancellationToken));
        }

        return new BulkDecryptResult(results);
    }

    private static void Validate(BulkDecryptRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.OutputDirectory);
        if (request.InputPaths.Count == 0)
        {
            throw new ArgumentException("At least one input PDF is required.", nameof(request));
        }

        foreach (var inputPath in request.InputPaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        }
    }

    private async Task<FileResult> DecryptOneAsync(
        BulkDecryptRequest request,
        string inputPath,
        List<string> candidates,
        IProgress<BulkProgress>? progress,
        int completedFiles,
        CancellationToken cancellationToken)
    {
        var totalFiles = request.InputPaths.Count;

        async Task<ProbeResult> ProbeAsync(string password, int attempt, int attemptCount)
        {
            progress?.Report(new BulkProgress(
                completedFiles, totalFiles, inputPath, BulkPhase.ProbingPasswords,
                attempt, attemptCount, null));
            return await QpdfPasswordProbe.ProbeAsync(
                request.QpdfPath, inputPath, password, cancellationToken);
        }

        var attemptCount = Math.Max(candidates.Count, 1);
        var emptyProbe = await ProbeAsync(string.Empty, 0, attemptCount);
        switch (emptyProbe.Outcome)
        {
            case ProbeOutcome.NotEncrypted:
                return await DecryptToOutputAsync(
                    request, inputPath, string.Empty, FileOutcome.NotEncrypted,
                    "No password. A copy was saved to the output folder.",
                    BulkPhase.Copying,
                    progress, completedFiles, 0, attemptCount, cancellationToken);
            case ProbeOutcome.Correct:
                return await DecryptToOutputAsync(
                    request, inputPath, string.Empty, FileOutcome.DecryptedNoPassword,
                    "No password was needed. Restrictions were removed.",
                    BulkPhase.Decrypting,
                    progress, completedFiles, 0, attemptCount, cancellationToken);
            case ProbeOutcome.Error:
                return FailedFromProbe(inputPath, emptyProbe);
        }

        for (var attempt = 0; attempt < candidates.Count; attempt++)
        {
            if (candidates[attempt].Length == 0)
            {
                continue;
            }

            var probe = await ProbeAsync(candidates[attempt], attempt + 1, candidates.Count);
            if (probe.Outcome == ProbeOutcome.Wrong)
            {
                continue;
            }

            if (probe.Outcome == ProbeOutcome.Error)
            {
                return FailedFromProbe(inputPath, probe);
            }

            var password = candidates[attempt];
            var result = await DecryptToOutputAsync(
                request, inputPath, password, FileOutcome.Decrypted,
                "The password was found and the PDF was decrypted.",
                BulkPhase.Decrypting,
                progress, completedFiles, attempt + 1, candidates.Count, cancellationToken);
            if (result.Outcome == FileOutcome.Decrypted)
            {
                candidates.RemoveAt(attempt);
                candidates.Insert(0, password);
            }

            return result;
        }

        var tried = candidates.Count;
        return new FileResult(
            inputPath,
            null,
            FileOutcome.NoPasswordMatched,
            null,
            tried == 0
                ? "This PDF needs a password, but no passwords were provided."
                : $"None of the {tried} passwords worked for this file.",
            $"{tried} passwords were tried without success.");
    }

    private async Task<FileResult> DecryptToOutputAsync(
        BulkDecryptRequest request,
        string inputPath,
        string password,
        FileOutcome successOutcome,
        string successMessage,
        BulkPhase phase,
        IProgress<BulkProgress>? progress,
        int completedFiles,
        int attempt,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        var totalFiles = request.InputPaths.Count;
        progress?.Report(new BulkProgress(
            completedFiles, totalFiles, inputPath, phase,
            attempt, attemptCount, 0));

        var outputPath = ResolveOutputPath(inputPath, request.OutputDirectory, request.ConflictPolicy);
        if (outputPath is null)
        {
            return new FileResult(
                inputPath, null, FileOutcome.Skipped, null,
                "A decrypted copy already exists, so this file was skipped.", string.Empty);
        }

        if (PathsEqual(outputPath, inputPath))
        {
            return new FileResult(
                inputPath, null, FileOutcome.Failed, null,
                "The decrypted copy would replace the original file. Choose a different output folder.",
                string.Empty);
        }

        var decryptProgress = progress is null
            ? null
            : new Progress<int>(percent => progress.Report(new BulkProgress(
                completedFiles, totalFiles, inputPath, phase,
                attempt, attemptCount, percent)));

        var result = await decryptService.DecryptAsync(
            new DecryptRequest(request.QpdfPath, inputPath, outputPath, password),
            decryptProgress,
            cancellationToken);

        if (!result.Succeeded)
        {
            return new FileResult(
                inputPath, null, FileOutcome.Failed, null, result.Message, result.Details);
        }

        return new FileResult(
            inputPath,
            outputPath,
            successOutcome,
            successOutcome == FileOutcome.Decrypted ? password : null,
            result.HasWarnings ? "Decrypted with warnings." : successMessage,
            result.Details);
    }

    private static FileResult FailedFromProbe(string inputPath, ProbeResult probe) => new(
        inputPath,
        null,
        FileOutcome.Failed,
        null,
        QpdfDecryptService.FriendlyError(probe.Error),
        probe.Error);

    private static string? ResolveOutputPath(string inputPath, string outputDirectory, ConflictPolicy policy)
    {
        var outputPath = Path.Combine(outputDirectory, Path.GetFileName(inputPath));
        if (!File.Exists(outputPath) || policy == ConflictPolicy.Overwrite)
        {
            return outputPath;
        }

        if (policy == ConflictPolicy.Skip)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(inputPath);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = Path.Combine(outputDirectory, $"{stem} ({suffix}).pdf");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
