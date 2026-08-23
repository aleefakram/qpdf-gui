using System.Diagnostics;

namespace QPdfDecryptor.Core;

internal enum ProbeOutcome
{
    Correct,
    Wrong,
    NotEncrypted,
    Error
}

internal sealed record ProbeResult(ProbeOutcome Outcome, string Error);

internal static class QpdfPasswordProbe
{
    public static async Task<ProbeResult> ProbeAsync(
        string qpdfPath,
        string inputPath,
        string password,
        CancellationToken cancellationToken)
    {
        var startInfo = QpdfProcessRunner.CreateStartInfo(qpdfPath);
        startInfo.ArgumentList.Add("--requires-password");
        startInfo.ArgumentList.Add("--password-file=-");
        startInfo.ArgumentList.Add(inputPath);

        var run = await QpdfProcessRunner.RunAsync(
            startInfo, password, null, cancellationToken);
        return run.ExitCode switch
        {
            3 => new ProbeResult(ProbeOutcome.Correct, run.Error),
            0 => new ProbeResult(ProbeOutcome.Wrong, run.Error),
            2 when string.IsNullOrWhiteSpace(run.Error) =>
                new ProbeResult(ProbeOutcome.NotEncrypted, string.Empty),
            _ => new ProbeResult(ProbeOutcome.Error, run.Error)
        };
    }
}
