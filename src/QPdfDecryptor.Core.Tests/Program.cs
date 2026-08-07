using QPdfDecryptor.Core;

if (args.Contains("--password-file=-", StringComparer.Ordinal))
{
    return await RunFakeQpdf(args);
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Password is sent through stdin", PasswordUsesStandardInput),
    ("Input and output must differ", RejectsSameOutput),
    ("Line breaks in passwords are rejected", RejectsPasswordLineBreak),
    ("Existing output survives process startup failure", ExistingOutputSurvivesStartupFailure),
    ("Wrong password preserves output and returns a useful error", WrongPasswordPreservesOutput),
    ("Cancellation preserves output and removes partial files", CancellationPreservesOutput)
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

    if (password != "correct")
    {
        await Console.Error.WriteLineAsync("qpdf: invalid password");
        return 2;
    }

    return 0;
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

sealed class CallbackProgress(Action<int> callback) : IProgress<int>
{
    public void Report(int value) => callback(value);
}
