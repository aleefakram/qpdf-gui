using System.Diagnostics;
using System.Text.RegularExpressions;

namespace QPdfDecryptor.Core;

public sealed partial class QpdfDecryptService
{
    public async Task<DecryptResult> DecryptAsync(
        DecryptRequest request,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var temporaryOutputPath = CreateTemporaryOutputPath(request.OutputPath);
        var processRequest = request with { OutputPath = temporaryOutputPath };
        var startInfo = CreateStartInfo(processRequest);

        try
        {
            var run = await QpdfProcessRunner.RunAsync(
                startInfo,
                request.Password,
                progress is null ? null : line => CaptureLine(line, progress),
                cancellationToken);

            var details = JoinDetails(run.Output, run.Error);
            var hasWarnings = run.ExitCode == 3;
            if ((run.ExitCode == 0 || hasWarnings) && File.Exists(temporaryOutputPath))
            {
                try
                {
                    File.Move(temporaryOutputPath, request.OutputPath, overwrite: true);
                    progress?.Report(100);
                    return DecryptResult.Success(hasWarnings, details);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return DecryptResult.Failure("The decrypted PDF could not be saved.", exception.Message);
                }
            }

            return DecryptResult.Failure(FriendlyError(details), details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            TryDeleteTemporaryOutput(temporaryOutputPath);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(DecryptRequest request)
    {
        var startInfo = QpdfProcessRunner.CreateStartInfo(request.QpdfPath);
        startInfo.ArgumentList.Add("--password-file=-");
        startInfo.ArgumentList.Add("--decrypt");
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add(request.InputPath);
        startInfo.ArgumentList.Add(request.OutputPath);
        return startInfo;
    }

    internal static string FriendlyError(string details)
    {
        if (details.Contains("invalid password", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("password is incorrect", StringComparison.OrdinalIgnoreCase))
        {
            return "The password is incorrect.";
        }

        if (details.Contains("not a PDF file", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("can't find PDF header", StringComparison.OrdinalIgnoreCase))
        {
            return "The selected file is not a valid PDF.";
        }

        if (details.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("used by another process", StringComparison.OrdinalIgnoreCase))
        {
            return "The output file cannot be written. Close it if it is open, or choose another location.";
        }

        return "The PDF could not be decrypted. Check the password and file, then try again.";
    }

    private static void Validate(DecryptRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.QpdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);

        if (request.Password.Contains('\r') || request.Password.Contains('\n'))
        {
            throw new ArgumentException("Passwords cannot contain a line break.", nameof(request));
        }

        if (Path.GetFullPath(request.InputPath).Equals(
                Path.GetFullPath(request.OutputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output must be a different file from the input.", nameof(request));
        }
    }

    private static void CaptureLine(string? line, IProgress<int>? progress)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var match = ProgressPattern().Match(line);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
        {
            progress?.Report(Math.Clamp(value, 0, 100));
        }
    }

    private static string JoinDetails(string output, string error) =>
        string.Join(Environment.NewLine, new[] { error.Trim(), output.Trim() }
            .Where(value => value.Length > 0));

    private static string CreateTemporaryOutputPath(string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new ArgumentException("The output path must include a directory.", nameof(outputPath));
        return Path.Combine(
            directory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteTemporaryOutput(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"write progress:\s*(\d+)%", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressPattern();
}
