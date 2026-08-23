using System.Diagnostics;
using QPdfDecryptor.Core;

if (args.Contains("--requires-password", StringComparer.Ordinal))
{
    return await RunFakeQpdfProbe(args);
}

if (args.Contains("--password-file=-", StringComparer.Ordinal))
{
    return await RunFakeQpdf(args);
}

if (args.Contains("--show-npages", StringComparer.Ordinal))
{
    Console.WriteLine(Environment.GetEnvironmentVariable("FAKE_QPDF_PAGES") ?? "1");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Password is sent through stdin", PasswordUsesStandardInput),
    ("Input and output must differ", RejectsSameOutput),
    ("Line breaks in passwords are rejected", RejectsPasswordLineBreak),
    ("Existing output survives process startup failure", ExistingOutputSurvivesStartupFailure),
    ("Wrong password preserves output and returns a useful error", WrongPasswordPreservesOutput),
    ("Cancellation preserves output and removes partial files", CancellationPreservesOutput),
    ("Probe reports a correct password", ProbeReportsCorrectPassword),
    ("Probe reports a wrong password", ProbeReportsWrongPassword),
    ("Probe reports an unencrypted file", ProbeReportsUnencryptedFile),
    ("Probe reports an unreadable file as an error", ProbeReportsUnreadableFileAsError),
    ("Password list parser handles BOM, line endings, blanks, and duplicates", ParserHandlesRealWorldLists),
    ("Bulk run decrypts every file with a single candidate", BulkSingleCandidateDecryptsEveryFile),
    ("Each file finds its own password", EachFileFindsItsOwnPassword),
    ("Exhausted list reports no match and writes nothing", ExhaustedListReportsNoMatch),
    ("Unencrypted file is copied to the output folder", UnencryptedFileIsCopied),
    ("Corrupt file fails without stopping the batch", CorruptFileFailsWithoutStoppingBatch),
    ("Winning password is tried first for the next file", WinningPasswordIsTriedFirstForNextFile),
    ("Owner-restricted file decrypts without a password", OwnerOnlyFileDecryptsWithoutPassword),
    ("Existing output is renamed when policy is AutoRename", ExistingOutputIsRenamed),
    ("Existing output is skipped when policy is Skip", ExistingOutputIsSkipped),
    ("Cancelling keeps finished outputs and leaves no temporary files", BulkCancellationKeepsFinishedOutputs),
    ("Recursive inputs preserve relative subpaths", RecursiveInputsPreserveRelativeSubpaths),
    ("Page range parses simple lists", ParsesSimpleList),
    ("Page range resolves z and reversed ranges", ResolvesZAndReversed),
    ("Page range rejects garbage and out-of-bounds", RejectsGarbageAndOutOfBounds),
    ("Page count service reads show-npages output", ReadsShowPagesOutput),
    ("Is valid agrees with parse outcomes", IsValidAgreesWithParse)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

return failures;

static Task PasswordUsesStandardInput()
{
    var request = new DecryptRequest("qpdf.exe", "in.pdf", "out.pdf", "do-not-log-me");
    var method = typeof(QpdfDecryptService).GetMethod(
        "CreateStartInfo",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("CreateStartInfo was not found.");
    var info = (System.Diagnostics.ProcessStartInfo)method.Invoke(null, new object[] { request })!;
    var arguments = info.ArgumentList.ToArray();

    Assert(arguments.Contains("--password-file=-"), "The stdin password option is missing.");
    Assert(!arguments.Any(value => value.Contains(request.Password)), "The password leaked into arguments.");
    Assert(info.RedirectStandardInput, "Standard input is not redirected.");
    return Task.CompletedTask;
}

static async Task RejectsSameOutput()
{
    var service = new QpdfDecryptService();
    await AssertThrows<ArgumentException>(() => service.DecryptAsync(
        new DecryptRequest("qpdf.exe", "same.pdf", "same.pdf", "password")));
}

static async Task RejectsPasswordLineBreak()
{
    var service = new QpdfDecryptService();
    await AssertThrows<ArgumentException>(() => service.DecryptAsync(
        new DecryptRequest("qpdf.exe", "in.pdf", "out.pdf", "first\nsecond")));
}

static async Task ExistingOutputSurvivesStartupFailure()
{
    var directory = CreateTestDirectory();
    try
    {
        var input = Path.Combine(directory, "input.pdf");
        var output = Path.Combine(directory, "existing-output.pdf");
        await File.WriteAllTextAsync(input, "input");
        await File.WriteAllTextAsync(output, "original output");

        var service = new QpdfDecryptService();
        var result = await service.DecryptAsync(new DecryptRequest(
            Path.Combine(directory, "missing-qpdf.exe"), input, output, "password"));

        Assert(!result.Succeeded, "A missing process unexpectedly succeeded.");
        Assert(File.Exists(output), "The existing output was deleted.");
        Assert(await File.ReadAllTextAsync(output) == "original output", "The existing output was changed.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task WrongPasswordPreservesOutput()
{
    var directory = CreateTestDirectory();
    try
    {
        var input = Path.Combine(directory, "wrong-password.pdf");
        var output = Path.Combine(directory, "existing-output.pdf");
        await File.WriteAllTextAsync(input, "wrong-password");
        await File.WriteAllTextAsync(output, "original output");

        var service = new QpdfDecryptService();
        var result = await service.DecryptAsync(new DecryptRequest(
            Environment.ProcessPath!, input, output, "incorrect"));

        Assert(!result.Succeeded, "An incorrect password unexpectedly succeeded.");
        Assert(result.Message == "The password is incorrect.", "The password error was not actionable.");
        Assert(await File.ReadAllTextAsync(output) == "original output", "The existing output was changed.");
        Assert(!Directory.EnumerateFiles(directory, ".*.tmp").Any(), "A temporary output was left behind.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task CancellationPreservesOutput()
{
    var directory = CreateTestDirectory();
    try
    {
        var input = Path.Combine(directory, "wait-for-cancellation.pdf");
        var output = Path.Combine(directory, "existing-output.pdf");
        await File.WriteAllTextAsync(input, "wait");
        await File.WriteAllTextAsync(output, "original output");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var progress = new CallbackProgress(value =>
        {
            if (value == 1)
            {
                cancellation.Cancel();
            }
        });
        var service = new QpdfDecryptService();

        await AssertThrows<OperationCanceledException>(() => service.DecryptAsync(
            new DecryptRequest(Environment.ProcessPath!, input, output, "correct"),
            progress,
            cancellation.Token));

        Assert(await File.ReadAllTextAsync(output) == "original output", "Cancellation changed the existing output.");
        Assert(!Directory.EnumerateFiles(directory, ".*.tmp").Any(), "Cancellation left a temporary output behind.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task BulkSingleCandidateDecryptsEveryFile()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "correct");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var inputs = new[]
        {
            Path.Combine(directory, "a1.pdf"),
            Path.Combine(directory, "a2.pdf")
        };
        foreach (var input in inputs)
        {
            await File.WriteAllTextAsync(input, "input");
        }

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            inputs,
            new[] { "correct" },
            outputDirectory,
            ConflictPolicy.Overwrite));

        Assert(result.Files.Count == 2, "The batch did not produce a result for every file.");
        Assert(result.Files.All(file => file.Outcome == FileOutcome.Decrypted),
            "Not every file was decrypted.");
        Assert(result.Files.All(file => file.MatchedPassword == "correct"),
            "The matched password was not reported.");
        Assert(result.Files.All(file => file.OutputPath is not null && File.Exists(file.OutputPath)),
            "A decrypted output file is missing.");
        Assert(!Directory.EnumerateFiles(outputDirectory, ".*.tmp").Any(),
            "A temporary output was left behind.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task EachFileFindsItsOwnPassword()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptDirectory = Path.Combine(directory, "accepted");
        Directory.CreateDirectory(acceptDirectory);
        await File.WriteAllLinesAsync(
            Path.Combine(acceptDirectory, "a1.pdf.passwords.txt"), new[] { "alpha" });
        await File.WriteAllLinesAsync(
            Path.Combine(acceptDirectory, "a2.pdf.passwords.txt"), new[] { "beta" });
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_DIR", acceptDirectory);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var inputs = new[]
        {
            Path.Combine(directory, "a1.pdf"),
            Path.Combine(directory, "a2.pdf")
        };
        foreach (var input in inputs)
        {
            await File.WriteAllTextAsync(input, "input");
        }

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            inputs,
            new[] { "alpha", "beta" },
            outputDirectory,
            ConflictPolicy.Overwrite));

        Assert(result.Files[0].Outcome == FileOutcome.Decrypted &&
               result.Files[0].MatchedPassword == "alpha",
            "The first file did not find its own password.");
        Assert(result.Files[1].Outcome == FileOutcome.Decrypted &&
               result.Files[1].MatchedPassword == "beta",
            "The second file did not find its own password.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_DIR", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ExhaustedListReportsNoMatch()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "omega");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var input = Path.Combine(directory, "locked.pdf");
        await File.WriteAllTextAsync(input, "input");

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            new[] { input },
            new[] { "alpha" },
            outputDirectory,
            ConflictPolicy.Overwrite));

        Assert(result.Files[0].Outcome == FileOutcome.NoPasswordMatched,
            "An exhausted password list did not report NoPasswordMatched.");
        Assert(result.Files[0].Message.Contains("None of the 1 passwords"),
            "The no-match message was not actionable.");
        Assert(!Directory.EnumerateFiles(outputDirectory).Any(),
            "An output file was written despite the failure.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task UnencryptedFileIsCopied()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "correct");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var input = Path.Combine(directory, "open-report.pdf");
        await File.WriteAllTextAsync(input, "input");

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            new[] { input },
            new[] { "correct" },
            outputDirectory,
            ConflictPolicy.Overwrite));

        Assert(result.Files[0].Outcome == FileOutcome.NotEncrypted,
            "An unencrypted file was not reported as NotEncrypted.");
        Assert(result.Files[0].MatchedPassword is null,
            "A matched password was reported for an unencrypted file.");
        var copied = Path.Combine(outputDirectory, "open-report.pdf");
        Assert(File.Exists(copied) && await File.ReadAllTextAsync(copied) == "partial output",
            "The unencrypted file was not copied to the output folder.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task CorruptFileFailsWithoutStoppingBatch()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "correct");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var corrupt = Path.Combine(directory, "corrupt-bad.pdf");
        var healthy = Path.Combine(directory, "healthy.pdf");
        await File.WriteAllTextAsync(corrupt, "input");
        await File.WriteAllTextAsync(healthy, "input");

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            new[] { corrupt, healthy },
            new[] { "correct" },
            outputDirectory,
            ConflictPolicy.Overwrite));

        Assert(result.Files[0].Outcome == FileOutcome.Failed,
            "A corrupt file did not report Failed.");
        Assert(result.Files[1].Outcome == FileOutcome.Decrypted,
            "The batch did not continue after a corrupt file.");
        Assert(!File.Exists(Path.Combine(outputDirectory, "corrupt-bad.pdf")),
            "A corrupt file produced an output.");
        Assert(!Directory.EnumerateFiles(outputDirectory, ".*.tmp").Any(),
            "A temporary output was left behind.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task OwnerOnlyFileDecryptsWithoutPassword()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "\n");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var input = Path.Combine(directory, "restricted.pdf");
        await File.WriteAllTextAsync(input, "input");

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            new[] { input },
            new[] { "alpha" },
            outputDirectory,
            ConflictPolicy.Overwrite));

        Assert(result.Files[0].Outcome == FileOutcome.DecryptedNoPassword,
            "An owner-restricted file was not reported as DecryptedNoPassword.");
        Assert(result.Files[0].MatchedPassword is null,
            "A matched password was reported for a file that needed none.");
        Assert(File.Exists(Path.Combine(outputDirectory, "restricted.pdf")),
            "No output was written for the restricted file.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task WinningPasswordIsTriedFirstForNextFile()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllLinesAsync(acceptFile, new[] { "beta" });
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);
        var logFile = Path.Combine(directory, "probes.log");
        Environment.SetEnvironmentVariable("FAKE_QPDF_LOG", logFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var inputs = new[]
        {
            Path.Combine(directory, "a1.pdf"),
            Path.Combine(directory, "a2.pdf")
        };
        foreach (var input in inputs)
        {
            await File.WriteAllTextAsync(input, "input");
        }

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            inputs,
            new[] { "alpha", "beta" },
            outputDirectory,
            ConflictPolicy.Overwrite));

        Assert(result.Files.All(file => file.Outcome == FileOutcome.Decrypted),
            "Not every file was decrypted.");

        var attempts = await File.ReadAllLinesAsync(logFile);
        Assert(string.Join("|", attempts) == "|alpha|beta||beta",
            $"The winning password was not tried first for the second file: {string.Join("|", attempts)}");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Environment.SetEnvironmentVariable("FAKE_QPDF_LOG", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ExistingOutputIsRenamed()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "correct");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var existingOutput = Path.Combine(outputDirectory, "a1.pdf");
        await File.WriteAllTextAsync(existingOutput, "existing output");
        var input = Path.Combine(directory, "a1.pdf");
        await File.WriteAllTextAsync(input, "input");

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            new[] { input },
            new[] { "correct" },
            outputDirectory,
            ConflictPolicy.AutoRename));

        Assert(result.Files[0].Outcome == FileOutcome.Decrypted, "The file was not decrypted.");
        Assert(result.Files[0].OutputPath == Path.Combine(outputDirectory, "a1 (2).pdf"),
            $"The output was not renamed: {result.Files[0].OutputPath}");
        Assert(await File.ReadAllTextAsync(existingOutput) == "existing output",
            "The existing output was changed.");
        Assert(File.Exists(result.Files[0].OutputPath!), "The renamed output is missing.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ExistingOutputIsSkipped()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "correct");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var existingOutput = Path.Combine(outputDirectory, "a1.pdf");
        await File.WriteAllTextAsync(existingOutput, "existing output");
        var input = Path.Combine(directory, "a1.pdf");
        await File.WriteAllTextAsync(input, "input");

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            new[] { input },
            new[] { "correct" },
            outputDirectory,
            ConflictPolicy.Skip));

        Assert(result.Files[0].Outcome == FileOutcome.Skipped,
            "An existing output was not reported as Skipped.");
        Assert(await File.ReadAllTextAsync(existingOutput) == "existing output",
            "The existing output was changed.");
        Assert(Directory.GetFiles(outputDirectory, "*.pdf").Length == 1,
            "An extra output was written.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task BulkCancellationKeepsFinishedOutputs()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "correct");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var first = Path.Combine(directory, "b1.pdf");
        var second = Path.Combine(directory, "wait-for-cancellation-b2.pdf");
        await File.WriteAllTextAsync(first, "input");
        await File.WriteAllTextAsync(second, "input");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var progress = new CallbackProgress<BulkProgress>(value =>
        {
            if (value.DecryptPercent == 1)
            {
                cancellation.Cancel();
            }
        });

        var service = new BulkDecryptService();
        await AssertThrows<OperationCanceledException>(() => service.DecryptAsync(
            new BulkDecryptRequest(
                Environment.ProcessPath!,
                new[] { first, second },
                new[] { "correct" },
                outputDirectory,
                ConflictPolicy.Overwrite),
            progress,
            cancellation.Token));

        Assert(File.Exists(Path.Combine(outputDirectory, "b1.pdf")),
            "The finished file's output was not kept.");
        Assert(!File.Exists(Path.Combine(outputDirectory, "wait-for-cancellation-b2.pdf")),
            "The cancelled file produced an output.");
        Assert(!Directory.EnumerateFiles(outputDirectory, ".*.tmp").Any(),
            "A temporary output was left behind.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task RecursiveInputsPreserveRelativeSubpaths()
{
    var directory = CreateTestDirectory();
    try
    {
        var acceptFile = Path.Combine(directory, "accepted.txt");
        await File.WriteAllTextAsync(acceptFile, "correct");
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", acceptFile);

        var outputDirectory = Path.Combine(directory, "out");
        Directory.CreateDirectory(outputDirectory);
        var root = Path.Combine(directory, "src");
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        var first = Path.Combine(root, "a", "same.pdf");
        var second = Path.Combine(root, "b", "same.pdf");
        await File.WriteAllTextAsync(first, "input");
        await File.WriteAllTextAsync(second, "input");

        var service = new BulkDecryptService();
        var result = await service.DecryptAsync(new BulkDecryptRequest(
            Environment.ProcessPath!,
            new[] { first, second },
            new[] { "correct" },
            outputDirectory,
            ConflictPolicy.Overwrite,
            root));

        Assert(result.Files.All(file => file.Outcome == FileOutcome.Decrypted),
            "Not every recursive file was decrypted.");
        Assert(File.Exists(Path.Combine(outputDirectory, "a", "same.pdf")) &&
               File.Exists(Path.Combine(outputDirectory, "b", "same.pdf")),
            "Recursive outputs were not preserved under relative subpaths.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE", null);
        Directory.Delete(directory, recursive: true);
    }
}

static async Task<int> RunFakeQpdf(string[] arguments)
{
    var password = await Console.In.ReadLineAsync() ?? string.Empty;
    var input = arguments[^2];
    var output = arguments[^1];
    await File.WriteAllTextAsync(output, "partial output");

    if (Path.GetFileName(input).StartsWith("wait-for-cancellation", StringComparison.Ordinal))
    {
        await Console.Out.WriteLineAsync("qpdf: write progress: 1%");
        await Console.Out.FlushAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    if (!IsAcceptedDecryptPassword(password, input))
    {
        await Console.Error.WriteLineAsync("qpdf: invalid password");
        return 2;
    }

    return 0;
}

static async Task<int> RunFakeQpdfProbe(string[] arguments)
{
    var password = await Console.In.ReadLineAsync() ?? string.Empty;
    var input = arguments[^1];
    await LogProbeAttempt(password);

    if (Path.GetFileName(input).StartsWith("open-", StringComparison.Ordinal))
    {
        return 2;
    }

    if (Path.GetFileName(input).StartsWith("corrupt-", StringComparison.Ordinal))
    {
        await Console.Error.WriteLineAsync("qpdf: can't find PDF header");
        return 2;
    }

    return IsAcceptedPassword(password, input) ? 3 : 0;
}

static bool IsAcceptedPassword(string password, string input)
{
    var acceptDirectory = Environment.GetEnvironmentVariable("FAKE_QPDF_ACCEPT_DIR");
    if (acceptDirectory is not null)
    {
        var perFile = Path.Combine(acceptDirectory, Path.GetFileName(input) + ".passwords.txt");
        return File.Exists(perFile) &&
               File.ReadAllLines(perFile).Contains(password, StringComparer.Ordinal);
    }

    var acceptFile = Environment.GetEnvironmentVariable("FAKE_QPDF_ACCEPT_FILE");
    return acceptFile is not null && File.Exists(acceptFile) &&
           File.ReadAllLines(acceptFile).Contains(password, StringComparer.Ordinal);
}

static bool IsAcceptedDecryptPassword(string password, string input) =>
    password == "correct" ||
    password.Length == 0 ||
    IsAcceptedPassword(password, input);

static async Task LogProbeAttempt(string password)
{
    var logFile = Environment.GetEnvironmentVariable("FAKE_QPDF_LOG");
    if (logFile is not null)
    {
        await File.AppendAllTextAsync(logFile, password + Environment.NewLine);
    }
}

static string CreateTestDirectory()
{
    var directory = Path.Combine(AppContext.BaseDirectory, "TestRuns", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static async Task AssertThrows<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task ProbeReportsCorrectPassword()
{
    var qpdf = FindRealQpdf();
    if (qpdf is null)
    {
        Console.WriteLine("SKIP ProbeReportsCorrectPassword (no qpdf.exe found)");
        return;
    }

    var directory = CreateTestDirectory();
    try
    {
        var encrypted = CreateEncryptedSample(qpdf, directory, "probe-secret");
        var result = await QpdfPasswordProbe.ProbeAsync(
            qpdf, encrypted, "probe-secret", CancellationToken.None);
        Assert(result.Outcome == ProbeOutcome.Correct, $"Expected Correct, got {result.Outcome}.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ProbeReportsWrongPassword()
{
    var qpdf = FindRealQpdf();
    if (qpdf is null)
    {
        Console.WriteLine("SKIP ProbeReportsWrongPassword (no qpdf.exe found)");
        return;
    }

    var directory = CreateTestDirectory();
    try
    {
        var encrypted = CreateEncryptedSample(qpdf, directory, "probe-secret");
        var result = await QpdfPasswordProbe.ProbeAsync(
            qpdf, encrypted, "not-the-password", CancellationToken.None);
        Assert(result.Outcome == ProbeOutcome.Wrong, $"Expected Wrong, got {result.Outcome}.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ProbeReportsUnencryptedFile()
{
    var qpdf = FindRealQpdf();
    if (qpdf is null)
    {
        Console.WriteLine("SKIP ProbeReportsUnencryptedFile (no qpdf.exe found)");
        return;
    }

    var directory = CreateTestDirectory();
    try
    {
        var plain = Path.Combine(directory, "plain.pdf");
        RunQpdf(qpdf, "--empty", plain);
        var result = await QpdfPasswordProbe.ProbeAsync(
            qpdf, plain, string.Empty, CancellationToken.None);
        Assert(result.Outcome == ProbeOutcome.NotEncrypted, $"Expected NotEncrypted, got {result.Outcome}.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task ProbeReportsUnreadableFileAsError()
{
    var qpdf = FindRealQpdf();
    if (qpdf is null)
    {
        Console.WriteLine("SKIP ProbeReportsUnreadableFileAsError (no qpdf.exe found)");
        return;
    }

    var result = await QpdfPasswordProbe.ProbeAsync(
        qpdf,
        Path.Combine(AppContext.BaseDirectory, "missing-file.pdf"),
        string.Empty,
        CancellationToken.None);
    Assert(result.Outcome == ProbeOutcome.Error, $"Expected Error, got {result.Outcome}.");
}

static async Task ParserHandlesRealWorldLists()
{
    var directory = CreateTestDirectory();
    try
    {
        var path = Path.Combine(directory, "passwords.txt");
        await File.WriteAllTextAsync(path, "\uFEFFfirst\r\n\r\nsecond\nsecond\r\n  spaced  \r");

        var passwords = PasswordListParser.Parse(path);

        Assert(passwords.Count == 3, $"Expected 3 unique passwords, got {passwords.Count}.");
        Assert(passwords[0] == "first", "The BOM was not stripped from the first password.");
        Assert(passwords[1] == "second", "Duplicate passwords were not removed.");
        Assert(passwords[2] == "  spaced  ", "Significant spaces were trimmed from a password.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task ParsesSimpleList()
{
    var pages = PageRangeParser.Parse("1-3,5", 12)
        ?? throw new Exception("expected parse");
    AssertSequence(pages, 1, 2, 3, 5);
    AssertSequence(PageRangeParser.Parse("2,2", 3)!, 2, 2);
    return Task.CompletedTask;
}

static Task ResolvesZAndReversed()
{
    AssertSequence(PageRangeParser.Parse("z", 3)!, 3);
    AssertSequence(PageRangeParser.Parse("Z", 3)!, 3);
    AssertSequence(PageRangeParser.Parse("z-1", 3)!, 3, 2, 1);
    AssertSequence(PageRangeParser.Parse("2-z", 4)!, 2, 3, 4);
    return Task.CompletedTask;
}

static Task RejectsGarbageAndOutOfBounds()
{
    foreach (var text in new[] { "", "  ", "0", "-2", "4", "1-", "a", "1--3", "1,,2", "1-,3" })
        if (PageRangeParser.Parse(text, 3) != null)
            throw new Exception($"'{text}' should be invalid");
    if (PageRangeParser.Parse("1", 0) != null)
        throw new Exception("'1' with a zero page count should be invalid");
    return Task.CompletedTask;
}

static async Task ReadsShowPagesOutput()
{
    var directory = CreateTestDirectory();
    try
    {
        Environment.SetEnvironmentVariable("FAKE_QPDF_PAGES", "7");
        try
        {
            var count = await PageCountService.GetPageCountAsync(
                Environment.ProcessPath!, Path.Combine(directory, "x.pdf"), CancellationToken.None);
            Assert(count == 7, $"Expected 7 pages, got {count}.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_QPDF_PAGES", null);
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task IsValidAgreesWithParse()
{
    Assert(PageRangeParser.IsValid("1-3,5,z", 12), "'1-3,5,z' should be valid for 12 pages.");
    Assert(PageRangeParser.IsValid("4", 12), "'4' should be valid for 12 pages, agreeing with Parse.");
    Assert(!PageRangeParser.IsValid("4", 3), "'4' should be invalid for 3 pages.");
    Assert(PageRangeParser.IsValid("1-z", int.MaxValue), "'1-z' should validate without expanding every page.");
    return Task.CompletedTask;
}

static void AssertSequence(IReadOnlyList<int> actual, params int[] expected)
{
    if (!actual.SequenceEqual(expected))
        throw new Exception($"[{string.Join(',', actual)}] != [{string.Join(',', expected)}]");
}

static string? FindRealQpdf()
{
    var fromEnvironment = Environment.GetEnvironmentVariable("QPDF_EXECUTABLE");
    if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
    {
        return fromEnvironment;
    }

    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, "src", "QPdfDecryptor", "Native", "qpdf.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        current = current.Parent;
    }

    return null;
}

static string CreateEncryptedSample(string qpdf, string directory, string password)
{
    var plain = Path.Combine(directory, "plain.pdf");
    var encrypted = Path.Combine(directory, "encrypted.pdf");
    RunQpdf(qpdf, "--empty", plain);
    RunQpdf(qpdf, "--encrypt", password, password, "256", "--", plain, encrypted);
    return encrypted;
}

static void RunQpdf(string qpdf, params string[] arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = qpdf,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("qpdf could not be started.");
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"qpdf {arguments[0]} failed with exit code {process.ExitCode}.");
    }
}

sealed class CallbackProgress(Action<int> callback) : IProgress<int>
{
    public void Report(int value) => callback(value);
}

sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
