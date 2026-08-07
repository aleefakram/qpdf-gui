using System.Diagnostics;
using System.Text;
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
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, eventArgs) =>
            CaptureLine(eventArgs.Data, output, progress);
        process.ErrorDataReceived += (_, eventArgs) =>
            CaptureLine(eventArgs.Data, error, progress);

        try
        {
            if (!process.Start())
            {
                return DecryptResult.Failure("qpdf could not be started.", string.Empty);
            }

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process completed between the check and Kill.
                }
            });

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Standard input keeps the password out of process arguments and temporary files.
            await process.StandardInput.WriteLineAsync(request.Password.AsMemory(), cancellationToken);
            process.StandardInput.Close();

            await process.WaitForExitAsync(cancellationToken);

            var details = JoinDetails(output, error);
            var hasWarnings = process.ExitCode == 3;
            if ((process.ExitCode == 0 || hasWarnings) && File.Exists(temporaryOutputPath))
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
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            return DecryptResult.Failure("qpdf could not process this file.", exception.Message);
        }
        finally
        {
            TryDeleteTemporaryOutput(temporaryOutputPath);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(DecryptRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.QpdfPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("--password-file=-");
        startInfo.ArgumentList.Add("--decrypt");
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add(request.InputPath);
        startInfo.ArgumentList.Add(request.OutputPath);
        return startInfo;
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

    private static void CaptureLine(string? line, StringBuilder destination, IProgress<int>? progress)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        destination.AppendLine(line);
        var match = ProgressPattern().Match(line);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
        {
            progress?.Report(Math.Clamp(value, 0, 100));
        }
    }

    private static string FriendlyError(string details)
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

    private static string JoinDetails(StringBuilder output, StringBuilder error) =>
        string.Join(Environment.NewLine, new[] { error.ToString().Trim(), output.ToString().Trim() }
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
