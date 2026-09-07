using QPdfDecryptor.Core;
using System.IO;

namespace QPdfDecryptor;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Fake-qpdf mode: when spawned as a child process with qpdf arguments (via
        // Environment.ProcessPath) and the FAKE_QPDF_* variables set, act as qpdf
        // instead of re-running the suite. Mirrors the Core.Tests harness pattern.
        if (args.Contains("--check") && Environment.GetEnvironmentVariable("FAKE_QPDF_CHECK_FAIL") is { } checkCoded)
        {
            return int.TryParse(checkCoded, out var checkExit) ? checkExit : 2;
        }

        if (args.Contains("--qdf") && Environment.GetEnvironmentVariable("FAKE_QPDF_QDF_JPEG") is { } fakeJpegPath)
        {
            return RunFakeQdf(args, fakeJpegPath);
        }

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
            ("Downsample decodes Flate RGB image", DownsampleDecodesFlateRgb),
            ("Downsample decodes Flate gray image", DownsampleDecodesFlateGray),
            ("Downsample rejects undecodable Flate", DownsampleRejectsBadFlate),
            ("Downsample reverses TIFF predictor", DownsampleReversesTiffPredictor),
            ("Downsample end-to-end shrinks a scan PDF", DownsampleEndToEndShrinksScan),
            ("Compress forwards staged input and surfaces note", CompressForwardsStagedInputAndSurfacesNote),
            ("Compress forwards Original resolution to staged run", CompressForwardsOriginalResolution),
            ("Downsample resolves indirect Resources maps", DownsampleResolvesIndirectResources),
            ("Compress reports note when nothing qualified", CompressNoteWhenNothingQualified),
            ("Compress phase channel resets after run", CompressPhaseChannelResetsFlag),
            ("Compress clears flags when staged run throws", CompressClearsFlagsWhenStagedThrows),
            ("Compress public ctor wraps run func", CompressPublicCtorWrapsRunFunc),
            ("Staged runner skips pre-pass when resolution is Original", StagedRunnerSkipsPrePassWhenOriginal),
            ("Staged runner feeds downsampled path and deletes workspace", StagedRunnerFeedsDownsampledPath),
            ("Staged runner falls through on pre-pass refusal", StagedRunnerFallsThroughOnRefusal),
            ("Staged runner deletes workspace when main run fails", StagedRunnerDeletesWorkspaceWhenMainRunFails),
            ("Staged runner deletes workspace when main run cancelled", StagedRunnerDeletesWorkspaceWhenMainRunCancelled),
            ("Staged runner propagates pre-pass cancel without main run", StagedRunnerPropagatesPrePassCancel),
            ("Downsample falls back when --check fails", DownsampleFallsBackWhenCheckFails),
            ("Downsample deletes workspace when pre-pass cancelled", DownsampleDeletesWorkspaceWhenPrePassCancelled),
            ("Staged runner feeds original input after check fallback", StagedRunnerFeedsOriginalInputAfterCheckFallback),
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

    private static Task DownsampleDecodesFlateRgb()
    {
        const int width = 8;
        const int height = 4;
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 7);
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionMode.Compress))
            {
                zlib.Write(pixels, 0, pixels.Length);
            }

            compressed = output.ToArray();
        }

        const string dict = "<< /Type /XObject /Subtype /Image /Width 8 /Height 4 " +
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length 0 >>";
        var decoded = Operations.ScanDownsampleService.DecodeFlateImage(compressed, dict, _ => null);
        Assert(decoded is not null, "valid Flate RGB must decode");
        Assert(decoded.Width == 8 && decoded.Height == 4 && !decoded.Gray, "wrong dims/space");
        Assert(decoded.Pixels.Length == pixels.Length, "wrong pixel length");
        // WIC Bgr24 order: R,G,B input becomes B,G,R output.
        Assert(decoded.Pixels[0] == pixels[2] && decoded.Pixels[1] == pixels[1] && decoded.Pixels[2] == pixels[0],
            "samples not swizzled to BGR");
        return Task.CompletedTask;
    }

    private static Task DownsampleDecodesFlateGray()
    {
        const int width = 8;
        const int height = 4;
        var pixels = new byte[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 3);
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionMode.Compress))
            {
                zlib.Write(pixels, 0, pixels.Length);
            }

            compressed = output.ToArray();
        }

        const string dict = "<< /Type /XObject /Subtype /Image /Width 8 /Height 4 " +
            "/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length 0 >>";
        var decoded = Operations.ScanDownsampleService.DecodeFlateImage(compressed, dict, _ => null);
        Assert(decoded is not null, "valid Flate gray must decode");
        Assert(decoded.Width == 8 && decoded.Height == 4 && decoded.Gray, "wrong dims/space");
        Assert(decoded.Pixels.Length == pixels.Length, "wrong pixel length");
        Assert(decoded.Pixels[0] == pixels[0], "gray samples must pass through");
        return Task.CompletedTask;
    }

    private static Task DownsampleRejectsBadFlate()
    {
        const string dict = "<< /Type /XObject /Subtype /Image /Width 8 /Height 4 " +
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length 4 >>";
        Assert(Operations.ScanDownsampleService.DecodeFlateImage(new byte[] { 0xFF, 0xD8, 0x00, 0x01 }, dict, _ => null) is null,
            "non-Flate bytes must be rejected");
        var pixels = new byte[8 * 4 * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 7);
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionMode.Compress))
            {
                zlib.Write(pixels, 0, pixels.Length);
            }

            compressed = output.ToArray();
        }

        Assert(Operations.ScanDownsampleService.DecodeFlateImage(compressed, dict.Replace("/DeviceRGB", "/DeviceCMYK"), _ => null) is null,
            "CMYK must be skipped");
        return Task.CompletedTask;
    }

    private static Task DownsampleReversesTiffPredictor()
    {
        // 4x1 RGB, predictor-2 encoded: each byte after the first pixel is a delta.
        byte[] plain = [10, 20, 30, 11, 21, 31, 12, 22, 32, 13, 23, 33];
        var encoded = new byte[plain.Length];
        Array.Copy(plain, encoded, plain.Length);
        for (var i = 3; i < encoded.Length; i++)
        {
            encoded[i] = (byte)(plain[i] - plain[i - 3]);
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionMode.Compress))
            {
                zlib.Write(encoded, 0, encoded.Length);
            }

            compressed = output.ToArray();
        }

        const string dict = "<< /Type /XObject /Subtype /Image /Width 4 /Height 1 " +
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode " +
            "/DecodeParms << /Predictor 2 /Columns 4 >> /Length 0 >>";
        var decoded = Operations.ScanDownsampleService.DecodeFlateImage(compressed, dict, _ => null);
        Assert(decoded is not null, "predictor-2 Flate must decode");
        Assert(decoded.Pixels[0] == 30 && decoded.Pixels[1] == 20 && decoded.Pixels[2] == 10,
            "first pixel wrong after reversal + BGR swizzle");
        Assert(decoded.Pixels[11] == 13 && decoded.Pixels[10] == 23 && decoded.Pixels[9] == 33,
            "last pixel wrong after reversal + BGR swizzle");
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
                // Default user path runs linearized: measure its overhead on the same fixture.
                var linearized = Path.Combine(workspace, "scan-linearized.pdf");
                var linOutcome = await QpdfOperationService.RunAsync(
                    new CompressRequest(qpdf, result.QdfPath!, true, linearized, 75),
                    null, CancellationToken.None, true);
                Assert(linOutcome.Succeeded, linOutcome.Details);
                var linAfter = new FileInfo(linearized).Length;
                Console.WriteLine($"SIZE-LIN {before} -> {linAfter} ({(double)linAfter / before:P0})");
                Assert((double)linAfter / before < 0.6, $"linearized expected <60% of original, got {linAfter} of {before}");
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

    private static async Task CompressForwardsStagedInputAndSurfacesNote()
    {
        Operations.StagedCompressInput? seen = null;
        var vm = new Operations.CompressViewModel(
            (input, progress, phase, cancellationToken) =>
            {
                seen = input;
                Assert(input.InputPath == "in.pdf" && input.MaxResolutionDpi == 150
                    && input.JpegQuality == 75 && input.AllowOverwrite == false
                    && input.OutputPath == "o.pdf" && input.LinearizeForWeb == true
                    && input.QpdfPath.Length > 0, "staged got wrong inputs");
                return Task.FromResult(new Operations.StagedCompressResult(
                    new OperationOutcome(true, false, 5, string.Empty, "ok"), "Downsampled 1 scan image to 150 DPI first."));
            });
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        vm.MaxResolutionDpi = 150;
        await vm.RunCommand.ExecuteAsync(null);
        Assert(seen is not null && seen.OutputPath == "o.pdf", "staged input forwarded");
        Assert(vm.Outcome?.Succeeded == true, "outcome surfaced");
        Assert(vm.DownsampleNote.Contains("Downsampled 1 scan image"), $"note missing: {vm.DownsampleNote}");
        Assert(!vm.IsBusy && !vm.IsDownsampling, "busy flags cleared");
    }

    private static async Task StagedRunnerDeletesWorkspaceWhenMainRunFails()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "pdf-ninja-downsample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var fakeQdf = Path.Combine(workspace, "work.qdf");
        await File.WriteAllTextAsync(fakeQdf, "fake");
        var runner = new Operations.StagedCompressRunner(
            (request, allowOverwrite, progress, cancellationToken) =>
                Task.FromException<OperationOutcome>(new InvalidOperationException("main boom")),
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
                Task.FromResult(new Operations.DownsampleResult(true, fakeQdf, 1, 1)));
        try
        {
            await runner.RunAsync(
                new Operations.StagedCompressInput("q.pdf", "in.pdf", "o.pdf", 150, 75, true, false),
                null, null, CancellationToken.None);
            Assert(false, "main-run failure must propagate");
        }
        catch (InvalidOperationException)
        {
        }

        Assert(!Directory.Exists(workspace), "workspace must be deleted after main-run failure");
    }

    private static async Task StagedRunnerDeletesWorkspaceWhenMainRunCancelled()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "pdf-ninja-downsample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var fakeQdf = Path.Combine(workspace, "work.qdf");
        await File.WriteAllTextAsync(fakeQdf, "fake");
        using var cts = new CancellationTokenSource();
        var runner = new Operations.StagedCompressRunner(
            (request, allowOverwrite, progress, cancellationToken) =>
                Task.FromException<OperationOutcome>(new OperationCanceledException(cts.Token)),
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
                Task.FromResult(new Operations.DownsampleResult(true, fakeQdf, 1, 1)));
        try
        {
            await runner.RunAsync(
                new Operations.StagedCompressInput("q.pdf", "in.pdf", "o.pdf", 150, 75, true, false),
                null, null, cts.Token);
            Assert(false, "cancel must propagate");
        }
        catch (OperationCanceledException)
        {
        }

        Assert(!Directory.Exists(workspace), "workspace must be deleted after main-run cancel");
    }

    private static async Task StagedRunnerPropagatesPrePassCancel()
    {
        var mainCalled = false;
        using var cts = new CancellationTokenSource();
        var runner = new Operations.StagedCompressRunner(
            (request, allowOverwrite, progress, cancellationToken) =>
            {
                mainCalled = true;
                return Task.FromResult(new OperationOutcome(true, false, 5, string.Empty, "ok"));
            },
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
                Task.FromException<Operations.DownsampleResult>(new OperationCanceledException(cts.Token)));
        try
        {
            await runner.RunAsync(
                new Operations.StagedCompressInput("q.pdf", "in.pdf", "o.pdf", 150, 75, true, false),
                null, null, cts.Token);
            Assert(false, "pre-pass cancel must propagate");
        }
        catch (OperationCanceledException)
        {
        }

        Assert(!mainCalled, "main run must not start after pre-pass cancel");
    }

    private static async Task DownsampleFallsBackWhenCheckFails()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "pdf-ninja-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var jpegPath = Path.Combine(workspace, "scan.jpg");
        await File.WriteAllBytesAsync(jpegPath, BuildGradientJpeg(1200, 1500, 92));
        Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_JPEG", jpegPath);
        Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_WIDTH", "1200");
        Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_HEIGHT", "1500");
        Environment.SetEnvironmentVariable("FAKE_QPDF_CHECK_FAIL", "2");
        try
        {
            // Self as qpdf: --qdf emits a hand-built QDF with one downsampleable
            // image; --check exits 2, so the service must report not-applied.
            var result = await Operations.ScanDownsampleService.DownsampleAsync(
                Environment.ProcessPath!, Path.Combine(workspace, "in.pdf"), 100, 75, null, CancellationToken.None);
            Assert(!result.Applied, "failed --check must fall back to not-applied");
            Assert(result.ImagesConsidered == 1, "splice path must have run before the --check gate");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_JPEG", null);
            Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_WIDTH", null);
            Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_HEIGHT", null);
            Environment.SetEnvironmentVariable("FAKE_QPDF_CHECK_FAIL", null);
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static async Task DownsampleDeletesWorkspaceWhenPrePassCancelled()
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
            var pdf = Path.Combine(workspace, "scan.pdf");
            WriteMinimalImagePdf(pdf, BuildGradientJpeg(1800, 2250, 92), 1800, 2250);
            var before = new HashSet<string>(Directory.GetDirectories(Path.GetTempPath(), "pdf-ninja-downsample-*"));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                await Operations.ScanDownsampleService.DownsampleAsync(
                    qpdf, pdf, 150, 75, null, cts.Token);
                Assert(false, "cancel must propagate");
            }
            catch (OperationCanceledException)
            {
            }

            var leaked = Directory.GetDirectories(Path.GetTempPath(), "pdf-ninja-downsample-*")
                .Where(d => !before.Contains(d)).ToList();
            Assert(leaked.Count == 0, $"workspace leaked: {string.Join(",", leaked)}");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static async Task StagedRunnerFeedsOriginalInputAfterCheckFallback()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "pdf-ninja-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var jpegPath = Path.Combine(workspace, "scan.jpg");
        await File.WriteAllBytesAsync(jpegPath, BuildGradientJpeg(1200, 1500, 92));
        var inputPdf = Path.Combine(workspace, "in.pdf");
        await File.WriteAllTextAsync(inputPdf, "input stays untouched");
        Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_JPEG", jpegPath);
        Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_WIDTH", "1200");
        Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_HEIGHT", "1500");
        Environment.SetEnvironmentVariable("FAKE_QPDF_CHECK_FAIL", "2");
        try
        {
            // Real service (self as fake qpdf) + stubbed main run: the --check
            // failure must hand the ORIGINAL input to the main stage.
            CompressRequest? seen = null;
            var runner = new Operations.StagedCompressRunner(
                (request, allowOverwrite, progress, cancellationToken) =>
                {
                    seen = request;
                    return Task.FromResult(new OperationOutcome(true, false, 5, string.Empty, "ok"));
                });
            var result = await runner.RunAsync(
                new Operations.StagedCompressInput(Environment.ProcessPath!, inputPdf,
                    Path.Combine(workspace, "o.pdf"), 100, 75, false, false),
                null, null, CancellationToken.None);
            Assert(seen is not null && seen.InputPath == inputPdf, "main run must receive the original input after fallback");
            Assert(result.Note.Contains("already at or below"), $"note missing: {result.Note}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_JPEG", null);
            Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_WIDTH", null);
            Environment.SetEnvironmentVariable("FAKE_QPDF_QDF_HEIGHT", null);
            Environment.SetEnvironmentVariable("FAKE_QPDF_CHECK_FAIL", null);
            Directory.Delete(workspace, recursive: true);
        }
    }

    // Fake-qpdf --qdf handler: emits a minimal QDF embedding the JPEG named by
    // FAKE_QPDF_QDF_JPEG so the real parser/splice path runs without real qpdf.
    private static int RunFakeQdf(string[] args, string jpegPath)
    {
        var output = args.LastOrDefault(a => !a.StartsWith('-'));
        if (output is null)
        {
            return 2;
        }

        var jpeg = File.ReadAllBytes(jpegPath);
        var width = int.TryParse(Environment.GetEnvironmentVariable("FAKE_QPDF_QDF_WIDTH"), out var w) ? w : 1200;
        var height = int.TryParse(Environment.GetEnvironmentVariable("FAKE_QPDF_QDF_HEIGHT"), out var h) ? h : 1500;
        const string content = "q 612 0 0 792 0 0 cm /Im1 Do Q";
        var latin1 = System.Text.Encoding.Latin1;
        using var file = File.Create(output);
        void Write(string value)
        {
            var encoded = latin1.GetBytes(value);
            file.Write(encoded, 0, encoded.Length);
        }

        Write("%PDF-1.7\n%QDF-1.0\n");
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
            "/Resources << /XObject << /Im1 5 0 R >> >> >>\nendobj\n");
        Write($"4 0 obj\n<< /Length {latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream\nendobj\n");
        Write($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
            $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        file.Write(jpeg, 0, jpeg.Length);
        Write("\nendstream\nendobj\n");
        return 0;
    }

    private static async Task CompressForwardsOriginalResolution()
    {
        var called = false;
        var vm = new Operations.CompressViewModel(
            (input, progress, phase, cancellationToken) =>
            {
                called = true;
                Assert(input.MaxResolutionDpi == 0, "resolution forwarded");
                return Task.FromResult(new Operations.StagedCompressResult(
                    new OperationOutcome(true, false, 5, string.Empty, "ok"), string.Empty));
            });
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        Assert(vm.MaxResolutionDpi == 0, "default must be Original");
        await vm.RunCommand.ExecuteAsync(null);
        Assert(called, "staged run invoked");
        Assert(vm.DownsampleNote.Length == 0, "no note when pre-pass skipped");
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
            (input, progress, phase, cancellationToken) =>
                Task.FromResult(new Operations.StagedCompressResult(
                    new OperationOutcome(true, false, 5, string.Empty, "ok"), "No large scans found — compressed without downsampling.")));
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        vm.MaxResolutionDpi = 150;
        await vm.RunCommand.ExecuteAsync(null);
        Assert(vm.DownsampleNote.Contains("No large scans found"), $"note missing: {vm.DownsampleNote}");
    }

    // Progress<T> posts callbacks asynchronously, so without a controlled context
    // this test could pass without ever observing a transition. Inline execution
    // makes every Report deterministic on the calling thread.
    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private static async Task CompressPhaseChannelResetsFlag()
    {
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
        try
        {
            var transitions = new List<bool>();
            var vm = new Operations.CompressViewModel(
                (input, progress, phase, cancellationToken) =>
                {
                    phase?.Report(true);
                    phase?.Report(false);
                    return Task.FromResult(new Operations.StagedCompressResult(
                        new OperationOutcome(true, false, 5, string.Empty, "ok"), string.Empty));
                });
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Operations.CompressViewModel.IsDownsampling))
                {
                    transitions.Add(vm.IsDownsampling);
                }
            };
            vm.InputPath = "in.pdf";
            vm.OutputPath = "o.pdf";
            vm.MaxResolutionDpi = 150;
            await vm.RunCommand.ExecuteAsync(null);
            Assert(vm.Outcome?.Succeeded == true, "outcome surfaced");
            Assert(transitions.Contains(true), "must observe the downsampling phase turn on");
            Assert(transitions.Count > 0 && transitions[^1] == false, "phase channel resets flag last");
            Assert(!vm.IsDownsampling, "final flag state is off");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
        }
    }

    private static async Task CompressClearsFlagsWhenStagedThrows()
    {
        var vm = new Operations.CompressViewModel(
            (input, progress, phase, cancellationToken) =>
                Task.FromException<Operations.StagedCompressResult>(new InvalidOperationException("boom")));
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        vm.MaxResolutionDpi = 150;
        try
        {
            await vm.RunCommand.ExecuteAsync(null);
            Assert(false, "staged throw must propagate");
        }
        catch (InvalidOperationException)
        {
        }

        Assert(!vm.IsBusy && !vm.IsDownsampling, "flags cleared after throw");
    }

    private static async Task CompressPublicCtorWrapsRunFunc()
    {
        CompressRequest? seen = null;
        var vm = new Operations.CompressViewModel(
            (request, allowOverwrite, progress, cancellationToken) =>
            {
                seen = request;
                return Task.FromResult(new OperationOutcome(true, false, 5, string.Empty, "ok"));
            });
        vm.InputPath = "in.pdf";
        vm.OutputPath = "o.pdf";
        await vm.RunCommand.ExecuteAsync(null); // MaxResolutionDpi defaults 0: pre-pass skipped, no qpdf needed
        Assert(seen is not null && seen.InputPath == "in.pdf", "public ctor run func invoked with original input");
        Assert(vm.Outcome?.Succeeded == true, "outcome surfaced");
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

    private static async Task StagedRunnerFeedsDownsampledPath()
    {
        CompressRequest? seen = null;
        var workspace = Path.Combine(Path.GetTempPath(), "pdf-ninja-downsample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var fakeQdf = Path.Combine(workspace, "work.qdf");
        await File.WriteAllTextAsync(fakeQdf, "fake");
        var runner = new Operations.StagedCompressRunner(
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
        var result = await runner.RunAsync(
            new Operations.StagedCompressInput("q.pdf", "in.pdf", "o.pdf", 150, 75, true, false),
            null, null, CancellationToken.None);
        Assert(seen is not null && seen.InputPath == fakeQdf, "run must use the downsampled file");
        Assert(result.Note.Contains("Downsampled 1 scan image"), $"note missing: {result.Note}");
        Assert(!Directory.Exists(workspace), "workspace must be deleted after success");
    }

    private static async Task StagedRunnerFallsThroughOnRefusal()
    {
        CompressRequest? seen = null;
        var runner = new Operations.StagedCompressRunner(
            (request, allowOverwrite, progress, cancellationToken) =>
            {
                seen = request;
                return Task.FromResult(new OperationOutcome(false, false, null,
                    "This PDF is password-protected. Use Decrypt first.", "probe refused"));
            },
            (qpdfPath, inputPath, maxDpi, jpegQuality, progress, cancellationToken) =>
                Task.FromException<Operations.DownsampleResult>(
                    new InvalidOperationException("This PDF is password-protected. Use Decrypt first.")));
        var result = await runner.RunAsync(
            new Operations.StagedCompressInput("q.pdf", "in.pdf", "o.pdf", 150, 75, true, false),
            null, null, CancellationToken.None); // must not throw
        Assert(seen is not null && seen.InputPath == "in.pdf", "main run must get the original input");
        Assert(result.Outcome.FriendlyError.Contains("password-protected"), "refusal must surface");
        Assert(result.Note.Length == 0, "no note on fall-through");
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
