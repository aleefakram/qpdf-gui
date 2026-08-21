using System.Diagnostics;
using System.Text;

namespace QPdfDecryptor.Core;

internal sealed record ProcessRunResult(int ExitCode, string Output, string Error);

internal static class QpdfProcessRunner
{
    public static ProcessStartInfo CreateStartInfo(string qpdfPath) => new()
    {
        FileName = qpdfPath,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    public static async Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo,
        string? stdinText,
        Action<string>? outputLine,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            output.AppendLine(eventArgs.Data);
            outputLine?.Invoke(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            error.AppendLine(eventArgs.Data);
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(-1, string.Empty, "qpdf could not be started.");
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

            if (stdinText is not null)
            {
                await process.StandardInput.WriteLineAsync(stdinText.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            // The parameterless WaitForExit drains the async output readers before we read the captured text.
            process.WaitForExit();
            return new ProcessRunResult(process.ExitCode, output.ToString(), error.ToString());
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

            return new ProcessRunResult(-1, string.Empty, exception.Message);
        }
    }
}
