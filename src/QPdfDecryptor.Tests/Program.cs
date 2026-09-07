using QPdfDecryptor.Core;
using System.IO;

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
            ("Downsample parses cm-Do draws", DownsampleDrawsParse),
            ("Downsample effective DPI math", DownsampleEffectiveDpi),
            ("Downsample image dict filter skips masks", DownsampleImageDictFilter),
            ("Downsample targets flag oversized scans", DownsampleTargetsFlagOversized),
            ("Downsample targets skip content under the cap", DownsampleTargetsSkipWhenUnderCap),
            ("Downsample rewrite dict updates dimensions", DownsampleRewriteDict),
            ("Downsample end-to-end shrinks a scan PDF", DownsampleEndToEndShrinksScan),
            ("Compress pre-pass feeds downsampled path into run", CompressPrePassFeedsDownsampledPath),
            ("Compress skips pre-pass when resolution is Original", CompressSkipsPrePassWhenOriginal),
            ("Downsample resolves indirect Resources maps", DownsampleResolvesIndirectResources),
            ("Compress reports note when nothing qualified", CompressNoteWhenNothingQualified),
            ("Compress pre-pass refusal falls through to main run", CompressPrePassRefusalFallsThrough),
            ("Staged runner skips pre-pass when resolution is Original", StagedRunnerSkipsPrePassWhenOriginal),
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

    private static Task DownsampleDrawsParse()
    {
        var draws = Operations.ScanDownsampleService.ParseContentDraws("q 612 0 0 792 0 0 cm /Im1 Do Q");
        Assert(draws.Count == 1, $"expected 1 draw, got {draws.Count}");
        Assert(draws[0].Name == "Im1", "wrong image name");
        Assert(Math.Abs(draws[0].A - 612) < 0.001 && Math.Abs(draws[0].D - 792) < 0.001, "wrong matrix");
        var split = Operations.ScanDownsampleService.ParseContentDraws("q\n612 0 0 792 0 0 cm\n/Im1 Do\nQ");
        Assert(split.Count == 1, "split-line draw missed");
        var bare = Operations.ScanDownsampleService.ParseContentDraws("q /Im1 Do Q");
        Assert(bare.Count == 0, "bare Do without cm must be ignored");
        return Task.CompletedTask;
    }

    private static Task DownsampleEffectiveDpi()
    {
        var dpi = Operations.ScanDownsampleService.EffectiveDpi(1200, 612);
        Assert(Math.Abs(dpi - 141.18) < 0.01, $"wrong dpi: {dpi}");
        Assert(Operations.ScanDownsampleService.EffectiveDpi(100, 0) == 0, "zero-size must yield 0");
        return Task.CompletedTask;
    }

    private static Task DownsampleImageDictFilter()
    {
        const string rgb = "<< /Type /XObject /Subtype /Image /Width 1200 /Height 1500 "
            + "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length 10 >>";
        Assert(Operations.ScanDownsampleService.IsImageDict(rgb, out var w, out var h) && w == 1200 && h == 1500,
            "rgb image must qualify");
        const string mask = "<< /Type /XObject /Subtype /Image /Width 1200 /Height 1500 /ImageMask true /Length 10 >>";
        Assert(!Operations.ScanDownsampleService.IsImageDict(mask, out _, out _), "mask must be skipped");
        const string smask = "<< /Type /XObject /Subtype /Image /Width 500 /Height 500 /SMask 9 0 R /Length 10 >>";
        Assert(!Operations.ScanDownsampleService.IsImageDict(smask, out _, out _), "soft mask must be skipped");
        return Task.CompletedTask;
    }

    private const string SyntheticQdf =
        "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
        "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
        "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
        "/Resources << /XObject << /Im1 5 0 R >> >> >>\nendobj\n" +
        "4 0 obj\n<< /Length 30 >>\nstream\nq 612 0 0 792 0 0 cm /Im1 Do Q\nendstream\nendobj\n" +
        "5 0 obj\n<< /Type /XObject /Subtype /Image /Width 1200 /Height 1500 /ColorSpace /DeviceRGB " +
        "/BitsPerComponent 8 /Filter /DCTDecode /Length 6 0 R >>\nstream\nÿØAAAA\nendstream\nendobj\n" +
        "6 0 obj\n4\nendobj\n";

    private static Task DownsampleTargetsFlagOversized()
    {
        var objects = Operations.ScanDownsampleService.ParseObjects(SyntheticQdf);
        Assert(objects.Count == 5, $"expected 5 parsed objects (bare length ref excluded), got {objects.Count}");
        var targets = Operations.ScanDownsampleService.FindDownsampleTargets(objects, SyntheticQdf, 100);
        Assert(targets.Count == 1 && targets.ContainsKey(5), "image 5 must be targeted at 100 DPI cap");
        Assert(targets[5].Width == 849 && targets[5].Height == 1062,
            $"wrong target size: {targets[5].Width}x{targets[5].Height}");
        return Task.CompletedTask;
    }

    private static Task DownsampleTargetsSkipWhenUnderCap()
    {
        var objects = Operations.ScanDownsampleService.ParseObjects(SyntheticQdf);
        var targets = Operations.ScanDownsampleService.FindDownsampleTargets(objects, SyntheticQdf, 150);
        Assert(targets.Count == 0, "141 DPI scan must be untouched at 150 DPI cap");
        return Task.CompletedTask;
    }

    private static Task DownsampleRewriteDict()
    {
        const string dict = "<< /Type /XObject /Subtype /Image /Width 1200 /Height 1500 "
            + "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length 7 0 R >>";
        var rewritten = Operations.ScanDownsampleService.RewriteImageDict(dict,
            new Operations.ScanDownsampleService.ImageReplacement(5, new byte[10], 600, 750, false));
        Assert(rewritten.Contains("/Width 600") && rewritten.Contains("/Height 750"), "dims not updated");
        Assert(rewritten.Contains("/Length 10"), "length not direct");
        Assert(rewritten.Contains("/Filter /DCTDecode"), "filter not normalized");
        return Task.CompletedTask;
    }

    private static async Task DownsampleEndToEndShrinksScan()
    {
        var qpdf = FindRepoQpdf();
        if (qpdf is null)
        {
            Console.WriteLine("SKIP qpdf.exe not found under repo Native/");
            return;
        }

        var workspace = Path.Combine(Path.GetTempPath(), "pdf-ninja-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var jpeg = BuildGradientJpeg(1800, 2250, 92);
            var pdf = Path.Combine(workspace, "scan.pdf");
            WriteMinimalImagePdf(pdf, jpeg, 1800, 2250);

            var result = await Operations.ScanDownsampleService.DownsampleAsync(
                qpdf, pdf, 150, 75, null, CancellationToken.None);
            Assert(result.Applied, "expected the scan to be downsampled");
            Assert(result.ImagesDownsampled == 1, $"expected 1 image, got {result.ImagesDownsampled}");
            Assert(result.QdfPath is not null && File.Exists(result.QdfPath), "qdf missing");
            try
            {
                var qdfBytes = await File.ReadAllBytesAsync(result.QdfPath!);
                var qdfText = System.Text.Encoding.Latin1.GetString(qdfBytes);
                Assert(qdfText.Contains("/Width 1275") && qdfText.Contains("/Height 1593"),
                    "expected 1275x1593 target dims in spliced QDF");
                Assert(qdfBytes.Length < new FileInfo(pdf).Length, "downsampled QDF should already be smaller");
                var compressed = Path.Combine(workspace, "scan-compressed.pdf");
                var outcome = await QpdfOperationService.RunAsync(
                    new CompressRequest(qpdf, result.QdfPath!, false, compressed, 75),
                    null, CancellationToken.None, true);
                Assert(outcome.Succeeded, outcome.Details);
                var before = new FileInfo(pdf).Length;
                var after = new FileInfo(compressed).Length;
                Console.WriteLine($"SIZE {before} -> {after} ({(double)after / before:P0})");
                Assert((double)after / before < 0.6, $"expected <60% of original, got {after} of {before}");
            }
            finally
            {
                Operations.ScanDownsampleService.DeleteWorkspaceFor(result.QdfPath!);
            }
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static async Task CompressPrePassFeedsDownsampledPath()
    {
        CompressRequest? seen = null;
        var fakeQdf = Path.Combine(Path.GetTempPath(), "fake-downsampled.qdf");
        var vm = new Operations.CompressViewModel(
            (request, allowOverwrite, progress, cancellationToken) =>
            {
                seen = request;
                return Task.FromResult(new OperationOutcome(true, false, 5, string.Empty, "ok"));
            },
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
            {
                Assert(inputPath == "in.pdf" && maxDpi == 150 && jpegQuality == 75, "pre-pass got wrong inputs");
                return Task.FromResult(new Operations.DownsampleResult(true, fakeQdf, 1, 1));
            });
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        vm.MaxResolutionDpi = 150;
        await vm.RunCommand.ExecuteAsync(null);
        Assert(seen is not null && seen.InputPath == fakeQdf, "run must use the downsampled file");
        Assert(vm.Outcome?.Succeeded == true, "outcome surfaced");
        Assert(vm.DownsampleNote.Contains("Downsampled 1 scan image"), $"note missing: {vm.DownsampleNote}");
    }

    private static async Task CompressSkipsPrePassWhenOriginal()
    {
        var prePassCalled = false;
        var vm = new Operations.CompressViewModel(
            (request, allowOverwrite, progress, cancellationToken) =>
                Task.FromResult(new OperationOutcome(true, false, 5, string.Empty, "ok")),
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
            {
                prePassCalled = true;
                return Task.FromResult(new Operations.DownsampleResult(false, null, 0, 0));
            });
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        Assert(vm.MaxResolutionDpi == 0, "default must be Original");
        await vm.RunCommand.ExecuteAsync(null);
        Assert(!prePassCalled, "pre-pass must not run when resolution is Original");
    }

    private static Task DownsampleResolvesIndirectResources()
    {
        // Real-world shape: /Resources lives in its own object, XObject map inline there.
        const string qdf =
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources 7 0 R >>\nendobj\n" +
            "4 0 obj\n<< /Length 30 >>\nstream\nq 612 0 0 792 0 0 cm /Im1 Do Q\nendstream\nendobj\n" +
            "5 0 obj\n<< /Type /XObject /Subtype /Image /Width 1200 /Height 1500 /ColorSpace /DeviceRGB " +
            "/BitsPerComponent 8 /Filter /DCTDecode /Length 4 >>\nstream\nÿØAAAA\nendstream\nendobj\n" +
            "7 0 obj\n<< /XObject << /Im1 5 0 R >> >>\nendobj\n";
        var objects = Operations.ScanDownsampleService.ParseObjects(qdf);
        var targets = Operations.ScanDownsampleService.FindDownsampleTargets(objects, qdf, 100);
        Assert(targets.Count == 1 && targets.ContainsKey(5), "indirect Resources must resolve to image 5");

        // Fully indirect shape: the XObject map itself is a separate object.
        const string qdf2 =
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources 7 0 R >>\nendobj\n" +
            "4 0 obj\n<< /Length 30 >>\nstream\nq 612 0 0 792 0 0 cm /Im1 Do Q\nendstream\nendobj\n" +
            "5 0 obj\n<< /Type /XObject /Subtype /Image /Width 1200 /Height 1500 /ColorSpace /DeviceRGB " +
            "/BitsPerComponent 8 /Filter /DCTDecode /Length 4 >>\nstream\nÿØAAAA\nendstream\nendobj\n" +
            "7 0 obj\n<< /XObject 8 0 R >>\nendobj\n" +
            "8 0 obj\n<< /Im1 5 0 R >>\nendobj\n";
        var objects2 = Operations.ScanDownsampleService.ParseObjects(qdf2);
        var targets2 = Operations.ScanDownsampleService.FindDownsampleTargets(objects2, qdf2, 100);
        Assert(targets2.Count == 1 && targets2.ContainsKey(5), "indirect XObject map must resolve to image 5");
        return Task.CompletedTask;
    }

    private static async Task CompressNoteWhenNothingQualified()
    {
        var vm = new Operations.CompressViewModel(
            (request, allowOverwrite, progress, cancellationToken) =>
                Task.FromResult(new OperationOutcome(true, false, 5, string.Empty, "ok")),
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
                Task.FromResult(new Operations.DownsampleResult(false, null, 0, 0)));
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        vm.MaxResolutionDpi = 150;
        await vm.RunCommand.ExecuteAsync(null);
        Assert(vm.DownsampleNote.Contains("No large scans found"), $"note missing: {vm.DownsampleNote}");
    }

    private static async Task CompressPrePassRefusalFallsThrough()
    {
        CompressRequest? seen = null;
        var vm = new Operations.CompressViewModel(
            (request, allowOverwrite, progress, cancellationToken) =>
            {
                seen = request;
                return Task.FromResult(new OperationOutcome(false, false, null,
                    "This PDF is password-protected. Use Decrypt first.", "probe refused"));
            },
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
                Task.FromException<Operations.DownsampleResult>(
                    new InvalidOperationException("This PDF is password-protected. Use Decrypt first.")));
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        vm.MaxResolutionDpi = 150;
        await vm.RunCommand.ExecuteAsync(null); // must not throw: refusal surfaces as Outcome
        Assert(seen is not null && seen.InputPath == "in.pdf", "main run must get the original input");
        Assert(vm.Outcome?.FriendlyError.Contains("password-protected") == true, "refusal must surface");
        Assert(!vm.IsBusy && !vm.IsDownsampling, "busy flags cleared");
    }

    private static async Task StagedRunnerSkipsPrePassWhenOriginal()
    {
        var prePassCalled = false;
        var runner = new Operations.StagedCompressRunner(
            (request, allowOverwrite, progress, cancellationToken) =>
                Task.FromResult(new OperationOutcome(true, false, 5, string.Empty, "ok")),
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
            {
                prePassCalled = true;
                return Task.FromResult(new Operations.DownsampleResult(false, null, 0, 0));
            });
        var result = await runner.RunAsync(
            new Operations.StagedCompressInput("q.pdf", "in.pdf", "o.pdf", 0, 75, true, false),
            null, null, CancellationToken.None);
        Assert(!prePassCalled, "pre-pass must not run when resolution is Original");
        Assert(result.Outcome.Succeeded, "outcome forwarded");
        Assert(result.Note.Length == 0, "note empty when pre-pass skipped");
    }

    private static string? FindRepoQpdf()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "QPdfDecryptor", "Native", "qpdf.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static byte[] BuildGradientJpeg(int width, int height, int quality)
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
        var stride = width * 3;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 3;
                pixels[i] = (byte)(x * 255 / width);
                pixels[i + 1] = (byte)(y * 255 / height);
                pixels[i + 2] = 128;
            }
        }

        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteMinimalImagePdf(string path, byte[] jpeg, int width, int height)
    {
        var latin1 = System.Text.Encoding.Latin1;
        const string content = "q 612 0 0 792 0 0 cm /Im1 Do Q";
        var contentBytes = latin1.GetBytes(content);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                "/Resources << /XObject << /Im1 5 0 R >> >> >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream",
        };
        using var file = File.Create(path);
        void Write(string value)
        {
            var encoded = latin1.GetBytes(value);
            file.Write(encoded, 0, encoded.Length);
        }

        Write("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(file.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        offsets.Add(file.Position);
        Write($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
            $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        file.Write(jpeg, 0, jpeg.Length);
        Write("\nendstream\nendobj\n");
        var xref = file.Position;
        var count = objects.Length + 2;
        Write($"xref\n0 {count}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
    }
}
