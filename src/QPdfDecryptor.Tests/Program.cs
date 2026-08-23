using QPdfDecryptor.Core;

namespace QPdfDecryptor;

public static class Program
{
    public static async Task<int> Main()
    {
        var failures = 0;
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Catalog registers seven destinations in three groups", CatalogRegistersSevenDestinations),
            ("Catalog groups are Secure Arrange Enhance", CatalogGroupsAreStable),
            ("Merge command gated on at least one file and output path", MergeGating),
            ("Merge run delegate produces outcome and toggles busy", MergeRunLifecycle),
            ("Split gating requires input, positive pages-per-file, folder and stem", SplitGating),
        };

        foreach (var test in tests)
        {
            try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}"); }
        }
        return failures;
    }

    private static Task CatalogRegistersSevenDestinations()
    {
        var ids = OperationCatalog.All.Select(operation => operation.Id).ToList();
        Assert(ids.Count == 7 && ids.Distinct().Count() == 7, "expected 7 unique destinations");
        Assert(ids.Contains("decrypt") && ids.Contains("merge") && ids.Contains("watermark"), "missing core ops");
        return Task.CompletedTask;
    }

    private static Task CatalogGroupsAreStable()
    {
        var groups = OperationCatalog.All.Select(o => o.Group).Distinct().ToList();
        Assert(groups.SequenceEqual(["Secure", "Arrange", "Enhance"]), $"unexpected groups: {string.Join(',', groups)}");
        Assert(OperationCatalog.Find("rotate").Group == "Arrange", "rotate belongs in Arrange");
        return Task.CompletedTask;
    }

    private static Task MergeGating()
    {
        var vm = new Operations.MergeViewModel();
        Assert(!vm.RunCommand.CanExecute(null), "must start disabled");
        vm.InputPaths.Add("C:\\tmp\\a.pdf");
        Assert(!vm.RunCommand.CanExecute(null), "still needs output path");
        vm.OutputPath = "C:\\tmp\\m.pdf";
        Assert(vm.RunCommand.CanExecute(null), "should be runnable now");
        return Task.CompletedTask;
    }

    private static async Task MergeRunLifecycle()
    {
        OperationOutcome? received = new(true, false, 5, string.Empty, "ok");
        var vm = new Operations.MergeViewModel((request, progress, cancellationToken) =>
        {
            Assert(request.InputPaths.Count == 1, "request should carry inputs");
            return Task.FromResult(received!);
        });
        vm.InputPaths.Add("a.pdf");
        vm.OutputPath = "m.pdf";
        await vm.RunCommand.ExecuteAsync(null);
        Assert(vm.Outcome == received, "outcome surfaced");
        Assert(!vm.IsBusy, "busy cleared after run");
    }

    private static Task SplitGating()
    {
        var vm = new Operations.SplitViewModel();
        Assert(!vm.RunCommand.CanExecute(null), "starts disabled");
        vm.InputPath = "C:\\tmp\\in.pdf";
        vm.PagesPerFile = 2;
        vm.OutputFolder = "C:\\tmp";
        vm.OutputStem = "scan";
        Assert(vm.RunCommand.CanExecute(null), "runnable after fill");
        vm.PagesPerFile = 0;
        Assert(!vm.RunCommand.CanExecute(null), "zero pages-per-file disables");
        return Task.CompletedTask;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
