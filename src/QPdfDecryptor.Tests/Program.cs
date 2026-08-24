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
            ("Rotate gating and range validation", RotateGating),
            ("Organize gating requires non-empty valid ranges", OrganizeGating),
            ("Compress defaults linearize on and gates on paths", CompressGating),
            ("Watermark placement flag maps overlay and underlay", WatermarkGating),
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
        var vm = new Operations.MergeViewModel((request, allowOverwrite, progress, cancellationToken) =>
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

    private static Task RotateGating()
    {
        var vm = new Operations.RotateViewModel();
        Assert(!vm.RunCommand.CanExecute(null), "starts disabled");
        vm.InputPath = "in.pdf"; vm.OutputPath = "out.pdf";
        Assert(vm.RunCommand.CanExecute(null), "defaults runnable");
        vm.PageRange = "99"; vm.KnownPageCount = 12;
        Assert(!vm.RunCommand.CanExecute(null), "out-of-bounds disables");
        if (vm.RangeError.Length == 0) throw new Exception("range error missing");
        vm.PageRange = "1-3,z";
        Assert(vm.RunCommand.CanExecute(null), "valid range runs");
        return Task.CompletedTask;
    }

    private static Task OrganizeGating()
    {
        var vm = new Operations.OrganizeViewModel();
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        vm.PageRanges = "";
        Assert(!vm.RunCommand.CanExecute(null), "empty ranges disable");
        if (vm.RangeError.Length == 0) throw new Exception("empty-range hint missing");
        vm.PageRanges = "z,1";
        vm.KnownPageCount = 5;
        Assert(vm.RunCommand.CanExecute(null), "reversed ranges valid");
        vm.PageRanges = "6";
        Assert(!vm.RunCommand.CanExecute(null), "6 of 5 pages disables");
        return Task.CompletedTask;
    }

    private static Task CompressGating()
    {
        var vm = new Operations.CompressViewModel();
        if (!vm.LinearizeForWeb) throw new Exception("default should be optimized-for-web ON");
        vm.InputPath = "in.pdf"; vm.OutputPath = "o.pdf";
        if (!vm.RunCommand.CanExecute(null)) throw new Exception("should run");
        return Task.CompletedTask;
    }

    private static Task WatermarkGating()
    {
        var vm = new Operations.WatermarkViewModel();
        Assert(!vm.RunCommand.CanExecute(null), "starts disabled");
        vm.InputPath = "in.pdf"; vm.WatermarkPdfPath = "wm.pdf"; vm.OutputPath = "o.pdf";
        if (!vm.RunCommand.CanExecute(null)) throw new Exception("front placement default runnable");
        var front = (string[])vm.CreateRequest(Operations.QpdfLocator.Path).BuildArguments("t");
        if (!front.Contains("--overlay") || front.Contains("--underlay")) throw new Exception("default must be overlay");
        vm.BehindContent = true;
        var behind = (string[])vm.CreateRequest(Operations.QpdfLocator.Path).BuildArguments("t");
        if (!behind.Contains("--underlay") || behind.Contains("--overlay")) throw new Exception("toggle failed");
        return Task.CompletedTask;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
