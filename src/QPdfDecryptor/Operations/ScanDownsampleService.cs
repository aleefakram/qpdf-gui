using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QPdfDecryptor.Core;

namespace QPdfDecryptor.Operations;

// Phase 1 scan compression: downsample oversized color/gray JPEG images via
// qpdf QDF surgery, then let the normal compress pipeline run on the result.
// v1 limits: DCTDecode (JPEG) and FlateDecode (direct RGB/gray, indexed)
// images only, drawn via a direct "cm ... Do"
// pair in a top-level page content stream. Skipped (left untouched):
// monochrome masks, soft-masked images, inline images, form-nested images,
// non-RGB/gray decodes, and images too small to matter.
internal sealed record DownsampleResult(bool Applied, string? QdfPath, int ImagesDownsampled, int ImagesConsidered);

internal static class ScanDownsampleService
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private static readonly Regex ObjHeaderPattern = new(@"(\d+)\s+(\d+)\s+obj\b", RegexOptions.Compiled);
    private static readonly Regex LengthPattern = new(@"/Length\s+(\d+)(?:\s+(\d+)\s+R)?", RegexOptions.Compiled);
    private static readonly Regex XObjectEntryPattern = new(@"/(\S+)\s+(\d+)\s+\d+\s+R", RegexOptions.Compiled);
    private static readonly Regex ContentsEntryPattern = new(@"(\d+)\s+\d+\s+R", RegexOptions.Compiled);
    private static readonly Regex PageTypePattern = new(@"/Type\s*/Page\b", RegexOptions.Compiled);
    private static readonly Regex ContentsPattern = new(@"/Contents\s*(\[(?:[^\[\]]*)\]|(?:\d+\s+\d+\s+R))",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DrawPattern = new(
        @"([-0-9.eE+]+)\s+([-0-9.eE+]+)\s+([-0-9.eE+]+)\s+([-0-9.eE+]+)\s+([-0-9.eE+]+)\s+([-0-9.eE+]+)\s+cm\s*/(\S+?)\s+Do\b",
        RegexOptions.Compiled);
    private static readonly Regex FilterPattern = new(@"/Filter\s*(\[[^\]]*\]|/\S+)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ColorSpacePattern = new(@"/ColorSpace\s*(\[[^\]]*\]|/\S+)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DecodeParmsPattern = new(@"/DecodeParms\s*(\[[^\]]*\]|<<.*?>>)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private const int MinPixelsToConsider = 400; // decorations and thumbnails stay untouched
    private const int MinTargetPixels = 8;

    public static async Task<DownsampleResult> DownsampleAsync(
        string qpdfPath,
        string inputPath,
        int maxDpi,
        int jpegQuality,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qpdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (maxDpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDpi));
        }

        var workspace = Path.Combine(Path.GetTempPath(), "pdf-ninja-downsample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var qdfPath = Path.Combine(workspace, "work.qdf");
        try
        {
            var qdfStart = QpdfProcessRunner.CreateStartInfo(qpdfPath);
            qdfStart.ArgumentList.Add("--qdf");
            qdfStart.ArgumentList.Add("--object-streams=disable");
            qdfStart.ArgumentList.Add("--decode-level=generalized");
            qdfStart.ArgumentList.Add(inputPath);
            qdfStart.ArgumentList.Add(qdfPath);
            var qdfRun = await QpdfProcessRunner.RunAsync(qdfStart, null, null, cancellationToken);
            if (qdfRun.ExitCode != 0)
            {
                if (qdfRun.Error.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    qdfRun.Error.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("This PDF is password-protected. Use Decrypt first.");
                }

                throw new InvalidOperationException("The PDF could not be read for scan optimization.");
            }

            var modified = await Task.Run(() =>
                TryDownsampleQdf(qdfPath, maxDpi, jpegQuality, progress, cancellationToken), cancellationToken);
            if (modified.ImagesDownsampled == 0)
            {
                return new DownsampleResult(false, null, 0, modified.ImagesConsidered);
            }

            var checkStart = QpdfProcessRunner.CreateStartInfo(qpdfPath);
            checkStart.ArgumentList.Add("--check");
            checkStart.ArgumentList.Add(qdfPath);
            var check = await QpdfProcessRunner.RunAsync(checkStart, null, null, cancellationToken);
            if (check.ExitCode is not (0 or 3))
            {
                // Splice produced something qpdf rejects: abandon quietly, the normal
                // compress pass still runs on the original input.
                return new DownsampleResult(false, null, 0, modified.ImagesConsidered);
            }

            var result = new DownsampleResult(true, qdfPath, modified.ImagesDownsampled, modified.ImagesConsidered);
            qdfPath = string.Empty; // ownership moves to the caller, which deletes it
            return result;
        }
        finally
        {
            if (qdfPath.Length > 0)
            {
                TryDeleteWorkspace(workspace);
            }
        }
    }

    internal static void DeleteWorkspaceFor(string qdfPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(qdfPath));
        if (directory is not null && Path.GetFileName(directory).StartsWith("pdf-ninja-downsample-", StringComparison.Ordinal))
        {
            TryDeleteWorkspace(directory);
        }
    }

    private static void TryDeleteWorkspace(string workspace)
    {
        try
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover temp must not fail the operation.
        }
    }

    // Returns per-run counts. Mutates the QDF file in place.
    internal static DownsampleCounts TryDownsampleQdf(
        string qdfPath,
        int maxDpi,
        int jpegQuality,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(qdfPath);
        var text = Latin1.GetString(bytes);
        var objects = ParseObjects(text);
        if (objects.Count == 0)
        {
            return new DownsampleCounts(0, 0);
        }

        var targets = FindDownsampleTargets(objects, text, maxDpi);
        if (targets.Count == 0)
        {
            return new DownsampleCounts(0, CountImageObjects(objects, text));
        }

        var rawCache = new Dictionary<int, string?>();
        string? ResolveRaw(int number)
        {
            if (!rawCache.TryGetValue(number, out var cached))
            {
                cached = FindRawObject(text, number);
                rawCache[number] = cached;
            }

            return cached;
        }

        var replacements = new List<ImageReplacement>(targets.Count);
        var done = 0;
        foreach (var (objectNumber, target) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = objects[objectNumber];
            var streamBytes = new byte[image.StreamLength];
            Buffer.BlockCopy(bytes, image.StreamOffset, streamBytes, 0, image.StreamLength);
            var dictText = text.Substring(image.DictOffset, image.DictLength);
            var replacement = TryReencode(streamBytes, dictText, target.Width, target.Height, jpegQuality,
                ResolveRaw);
            if (replacement is not null)
            {
                replacements.Add(new ImageReplacement(objectNumber, replacement.Bytes, target.Width, target.Height, replacement.Gray));
            }

            done++;
            progress?.Report(done * 100 / targets.Count);
        }

        if (replacements.Count == 0)
        {
            return new DownsampleCounts(0, targets.Count);
        }

        File.WriteAllBytes(qdfPath, SpliceImages(bytes, text, objects, replacements));
        return new DownsampleCounts(replacements.Count, targets.Count);
    }

    internal sealed record DownsampleCounts(int ImagesDownsampled, int ImagesConsidered);

    internal static int CountImageObjects(Dictionary<int, QdfObject> objects, string text)
    {
        var count = 0;
        foreach (var obj in objects.Values)
        {
            if (obj.HasStream && IsImageDict(text.Substring(obj.DictOffset, obj.DictLength), out _, out _))
            {
                count++;
            }
        }

        return count;
    }

    internal sealed record QdfObject(int Number, int DictOffset, int DictLength,
        bool HasStream, int StreamOffset, int StreamLength);

    internal sealed record ImageDraw(double A, double B, double C, double D, string Name);

    internal sealed record TargetSize(int Width, int Height);

    internal sealed record ImageReplacement(int ObjectNumber, byte[] JpegBytes, int Width, int Height, bool Gray);

    private sealed record ReencodedImage(byte[] Bytes, bool Gray);

    internal static Dictionary<int, QdfObject> ParseObjects(string text)
    {
        var headers = ObjHeaderPattern.Matches(text).Cast<Match>().ToList();
        var objects = new Dictionary<int, QdfObject>(headers.Count);
        for (var index = 0; index < headers.Count; index++)
        {
            var number = int.Parse(headers[index].Groups[1].Value);
            var bodyStart = headers[index].Index + headers[index].Length;
            var bodyEnd = index + 1 < headers.Count ? headers[index + 1].Index : text.Length;
            var obj = ParseObjectBody(number, text, bodyStart, bodyEnd);
            if (obj is not null)
            {
                objects[number] = obj;
            }
        }

        return objects;
    }

    private static QdfObject? ParseObjectBody(int number, string text, int bodyStart, int bodyEnd)
    {
        var dictStart = text.IndexOf("<<", bodyStart, bodyEnd - bodyStart, StringComparison.Ordinal);
        if (dictStart < 0)
        {
            return null; // bare value (e.g. a lone indirect length)
        }

        var dictEnd = FindDictEnd(text, dictStart, bodyEnd);
        if (dictEnd < 0)
        {
            return null;
        }

        var afterDict = SkipWhitespace(text, dictEnd, bodyEnd);
        if (!IsKeywordAt(text, afterDict, bodyEnd, "stream"))
        {
            return new QdfObject(number, dictStart, dictEnd - dictStart, false, 0, 0);
        }

        var dataStart = SkipStreamEol(text, afterDict + "stream".Length, bodyEnd);
        if (dataStart < 0)
        {
            return null;
        }

        var dictText = text.Substring(dictStart, dictEnd - dictStart);
        var length = ResolveLength(dictText, text);
        if (length is null || length < 0 || dataStart + length.Value > bodyEnd)
        {
            return null;
        }

        return new QdfObject(number, dictStart, dictEnd - dictStart, true, dataStart, length.Value);
    }

    private static int FindDictEnd(string text, int start, int limit)
    {
        var depth = 0;
        var index = start;
        while (index + 1 < limit)
        {
            if (text[index] == '<' && text[index + 1] == '<')
            {
                depth++;
                index += 2;
            }
            else if (text[index] == '>' && text[index + 1] == '>')
            {
                depth--;
                index += 2;
                if (depth == 0)
                {
                    return index;
                }
            }
            else
            {
                index++;
            }
        }

        return -1;
    }

    private static int? ResolveLength(string dictText, string fullText)
    {
        var match = LengthPattern.Match(dictText);
        if (!match.Success)
        {
            return null;
        }

        if (!match.Groups[2].Success)
        {
            return int.Parse(match.Groups[1].Value);
        }

        // Indirect length: find the referenced object's bare integer value.
        var refNumber = match.Groups[1].Value;
        var refHeader = Regex.Match(fullText, $@"(?<!\d){refNumber}\s+\d+\s+obj\b");
        if (!refHeader.Success)
        {
            return null;
        }

        var valueStart = refHeader.Index + refHeader.Length;
        var valueMatch = Regex.Match(fullText.Substring(valueStart, Math.Min(64, fullText.Length - valueStart)), @"\s*(\d+)");
        return valueMatch.Success ? int.Parse(valueMatch.Groups[1].Value) : null;
    }

    // Ad-hoc raw-object lookup for small text-valued references (palette
    // streams, DecodeParms). Limitation: a binary stream containing "endobj"
    // can truncate the match — acceptable because only small text-valued
    // lookups are resolved this way.
    internal static string? FindRawObject(string text, int number)
    {
        var match = Regex.Match(text, $@"(?<!\d){number}\s+\d+\s+obj\b(.*?)\s+endobj", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int SkipWhitespace(string text, int index, int limit)
    {
        while (index < limit && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsKeywordAt(string text, int index, int limit, string keyword)
    {
        if (index + keyword.Length > limit)
        {
            return false;
        }

        for (var i = 0; i < keyword.Length; i++)
        {
            if (text[index + i] != keyword[i])
            {
                return false;
            }
        }

        var after = index + keyword.Length;
        return after >= limit || !char.IsLetterOrDigit(text[after]);
    }

    private static int SkipStreamEol(string text, int index, int limit)
    {
        // PDF spec: EOL after "stream" is CRLF or LF (a lone CR is tolerated here).
        if (index < limit && text[index] == '\r')
        {
            index++;
        }

        return index < limit && text[index] == '\n' ? index + 1 : -1;
    }

    internal static bool IsImageDict(string dictText, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!Regex.IsMatch(dictText, @"/Subtype\s*/Image\b"))
        {
            return false;
        }

        if (Regex.IsMatch(dictText, @"/ImageMask\s*true\b"))
        {
            return false;
        }

        if (dictText.Contains("/SMask", StringComparison.Ordinal))
        {
            return false; // transparency group: leave soft-masked images alone
        }

        var widthMatch = Regex.Match(dictText, @"/Width\s+(\d+)");
        var heightMatch = Regex.Match(dictText, @"/Height\s+(\d+)");
        if (!widthMatch.Success || !heightMatch.Success)
        {
            return false;
        }

        width = int.Parse(widthMatch.Groups[1].Value);
        height = int.Parse(heightMatch.Groups[1].Value);
        return width > 0 && height > 0;
    }

    internal static List<ImageDraw> ParseContentDraws(string content)
    {
        var draws = new List<ImageDraw>();
        // Handles both single-line and split-across-lines "a b c d e f cm /Name Do".
        foreach (Match match in DrawPattern.Matches(content))
        {
            draws.Add(new ImageDraw(
                double.Parse(match.Groups[1].Value),
                double.Parse(match.Groups[2].Value),
                double.Parse(match.Groups[3].Value),
                double.Parse(match.Groups[4].Value),
                match.Groups[7].Value));
        }

        return draws;
    }

    internal static double EffectiveDpi(int pixels, double drawnPoints) =>
        drawnPoints > 0 ? pixels * 72.0 / drawnPoints : 0;

    // Object number -> target pixel size. Keeps the tightest scale when an image
    // is drawn more than once.
    internal static Dictionary<int, TargetSize> FindDownsampleTargets(
        Dictionary<int, QdfObject> objects, string text, int maxDpi)
    {
        var targets = new Dictionary<int, TargetSize>();
        foreach (var (number, obj) in objects)
        {
            var dictText = text.Substring(obj.DictOffset, obj.DictLength);
            if (!PageTypePattern.IsMatch(dictText))
            {
                continue;
            }

            var xobjects = FindXObjectMap(dictText, objects, text);
            if (xobjects.Count == 0)
            {
                continue;
            }

            foreach (var contentNumber in ExtractContentNumbers(dictText))
            {
                if (!objects.TryGetValue(contentNumber, out var content) || !content.HasStream)
                {
                    continue;
                }

                var contentDict = text.Substring(content.DictOffset, content.DictLength);
                if (contentDict.Contains("/Filter", StringComparison.Ordinal))
                {
                    continue; // still encoded: cannot read draws safely
                }

                var contentText = text.Substring(content.StreamOffset, content.StreamLength);
                foreach (var draw in ParseContentDraws(contentText))
                {
                    if (!xobjects.TryGetValue(draw.Name, out var imageNumber) ||
                        !objects.TryGetValue(imageNumber, out var image) || !image.HasStream)
                    {
                        continue;
                    }

                    var imageDict = text.Substring(image.DictOffset, image.DictLength);
                    if (!IsImageDict(imageDict, out var width, out var height) ||
                        width < MinPixelsToConsider || height < MinPixelsToConsider)
                    {
                        continue;
                    }

                    var drawnWidth = Math.Sqrt(draw.A * draw.A + draw.B * draw.B);
                    var drawnHeight = Math.Sqrt(draw.C * draw.C + draw.D * draw.D);
                    var dpiX = EffectiveDpi(width, drawnWidth);
                    var dpiY = EffectiveDpi(height, drawnHeight);
                    if (dpiX <= 0 || dpiY <= 0)
                    {
                        continue;
                    }

                    var scale = Math.Min(maxDpi / dpiX, maxDpi / dpiY);
                    if (scale >= 1)
                    {
                        continue;
                    }

                    var targetWidth = Math.Max(MinTargetPixels, (int)(width * scale));
                    var targetHeight = Math.Max(MinTargetPixels, (int)(height * scale));
                    if (targetWidth >= width && targetHeight >= height)
                    {
                        continue;
                    }

                    // Shared image across draws: keep the LARGEST required target so a
                    // thumbnail use can never destroy the full-page rendering.
                    if (!targets.TryGetValue(imageNumber, out var existing) ||
                        targetWidth * (long)targetHeight > existing.Width * (long)existing.Height)
                    {
                        targets[imageNumber] = new TargetSize(targetWidth, targetHeight);
                    }
                }
            }
        }

        return targets;
    }

    // Real-world PDFs usually keep /Resources (and sometimes the /XObject map
    // itself) in indirect objects; v1 only handled the fully-inline shape.
    internal static Dictionary<string, int> FindXObjectMap(string pageDictText,
        Dictionary<int, QdfObject> objects, string text)
    {
        var inline = ExtractXObjectMap(pageDictText);
        if (inline.Count > 0)
        {
            return inline;
        }

        var resources = ResolveReferenceDict(pageDictText, "Resources", objects, text);
        if (resources is not null)
        {
            var fromResources = ExtractXObjectMap(resources);
            if (fromResources.Count > 0)
            {
                return fromResources;
            }

            var indirectMap = ResolveReferenceDict(resources, "XObject", objects, text);
            if (indirectMap is not null)
            {
                return ParseXObjectEntries(indirectMap);
            }
        }

        var directMap = ResolveReferenceDict(pageDictText, "XObject", objects, text);
        return directMap is not null ? ParseXObjectEntries(directMap) : inline;
    }

    internal static string? ResolveReferenceDict(string dictText, string key,
        Dictionary<int, QdfObject> objects, string text)
    {
        var match = Regex.Match(dictText, $@"/{key}\s+(\d+)\s+\d+\s+R");
        if (!match.Success || !objects.TryGetValue(int.Parse(match.Groups[1].Value), out var target))
        {
            return null;
        }

        return text.Substring(target.DictOffset, target.DictLength);
    }

    internal static Dictionary<string, int> ParseXObjectEntries(string mapDictText)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match entry in XObjectEntryPattern.Matches(mapDictText))
        {
            map[entry.Groups[1].Value] = int.Parse(entry.Groups[2].Value);
        }

        return map;
    }

    internal static Dictionary<string, int> ExtractXObjectMap(string pageDictText)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var xobjectIndex = pageDictText.IndexOf("/XObject", StringComparison.Ordinal);
        if (xobjectIndex < 0)
        {
            return map;
        }

        var innerStart = pageDictText.IndexOf("<<", xobjectIndex, StringComparison.Ordinal);
        if (innerStart < 0)
        {
            return map;
        }

        var innerEnd = FindDictEnd(pageDictText, innerStart, pageDictText.Length);
        if (innerEnd < 0)
        {
            return map;
        }

        foreach (Match entry in XObjectEntryPattern.Matches(pageDictText.Substring(innerStart, innerEnd - innerStart)))
        {
            map[entry.Groups[1].Value] = int.Parse(entry.Groups[2].Value);
        }

        return map;
    }

    internal static List<int> ExtractContentNumbers(string pageDictText)
    {
        var numbers = new List<int>();
        var match = ContentsPattern.Match(pageDictText);
        if (!match.Success)
        {
            return numbers;
        }

        foreach (Match entry in ContentsEntryPattern.Matches(match.Groups[1].Value))
        {
            numbers.Add(int.Parse(entry.Groups[1].Value));
        }

        return numbers;
    }

    private static int? MatchInt(string dictText, string key)
    {
        var match = Regex.Match(dictText, $@"/{key}\s+(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private static ReencodedImage? TryReencode(byte[] source, string dictText, int targetWidth, int targetHeight,
        int jpegQuality, Func<int, string?> refResolver)
    {
        try
        {
            BitmapSource bitmap;
            bool gray;
            if (source.Length >= 2 && source[0] == 0xFF && source[1] == 0xD8)
            {
                using var input = new MemoryStream(source, writable: false);
                var decoder = new JpegBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return null;
                }

                var frame = decoder.Frames[0];
                gray = frame.Format == PixelFormats.Gray8;
                if (!gray && frame.Format != PixelFormats.Bgr24 && frame.Format != PixelFormats.Bgr32 &&
                    frame.Format != PixelFormats.Bgra32)
                {
                    return null; // CMYK and friends: leave alone rather than shift colors
                }

                bitmap = frame;
            }
            else
            {
                DecodedFlate? decoded;
                if (dictText.Contains("/Filter", StringComparison.Ordinal))
                {
                    decoded = DecodeFlateImage(source, dictText, refResolver);
                }
                else
                {
                    decoded = DecodeRawSamples(source, dictText, refResolver);
                }

                if (decoded is null)
                {
                    return null;
                }

                gray = decoded.Gray;
                bitmap = BitmapSource.Create(decoded.Width, decoded.Height, 96, 96,
                    gray ? PixelFormats.Gray8 : PixelFormats.Bgr24, null,
                    decoded.Pixels, decoded.Width * (gray ? 1 : 3));
                bitmap.Freeze();
            }

            if (bitmap.PixelWidth <= targetWidth && bitmap.PixelHeight <= targetHeight)
            {
                return null;
            }

            var scaled = new TransformedBitmap(bitmap, new ScaleTransform(
                targetWidth / (double)bitmap.PixelWidth, targetHeight / (double)bitmap.PixelHeight));
            scaled.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) };
            encoder.Frames.Add(BitmapFrame.Create(scaled));
            using var output = new MemoryStream();
            encoder.Save(output);
            var encoded = output.ToArray();
            if (encoded.Length >= source.Length)
            {
                return null; // no win: keep the original bytes
            }

            return new ReencodedImage(encoded, gray);
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    internal sealed record DecodedFlate(byte[] Pixels, int Width, int Height, bool Gray);

    private const long MaxDecompressedBytes = 256L * 1024 * 1024; // zip-bomb guard

    // Decodes FlateDecode image streams to WIC-ready pixels (Bgr24, or Gray8).
    // Returns null for anything outside v1 scope: the image stays untouched.
    internal static DecodedFlate? DecodeFlateImage(byte[] streamBytes, string dictText, Func<int, string?> refResolver)
    {
        byte[] raw;
        try
        {
            using var input = new MemoryStream(streamBytes, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            int read;
            long total = 0;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > MaxDecompressedBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            raw = output.ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return null; // not a Flate stream
        }

        return InterpretSamples(raw, dictText, refResolver, applyDecodeParms: true);
    }

    internal static DecodedFlate? DecodeRawSamples(byte[] raw, string dictText, Func<int, string?> refResolver) =>
        InterpretSamples(raw, dictText, refResolver, applyDecodeParms: false);

    internal static DecodedFlate? InterpretSamples(byte[] raw, string dictText, Func<int, string?> refResolver,
        bool applyDecodeParms)
    {
        var width = MatchInt(dictText, "Width");
        var height = MatchInt(dictText, "Height");
        if (width is null || height is null || width <= 0 || height <= 0)
        {
            return null;
        }

        int predictor;
        if (applyDecodeParms)
        {
            predictor = MatchInt(DecodeParmsText(dictText, refResolver), "Predictor") ?? 1;
            if (predictor is not (1 or 2))
            {
                return null; // PNG predictors 10-15 deferred
            }
        }
        else
        {
            predictor = 1; // filterless data is already raw: stale parms must not shift pixels
        }

        if (!TryDescribePixels(dictText, refResolver, width.Value, out var components, out var indexBpc, out var palette, out var gray))
        {
            return null;
        }

        var rowBytes = (width.Value * (indexBpc > 0 ? indexBpc : 8 * components) + 7) / 8;
        if (raw.Length != rowBytes * height.Value)
        {
            return null; // stride mismatch: never guess
        }

        var samples = ApplyPredictor(raw, width.Value, height.Value, rowBytes, indexBpc > 0 ? 1 : components, predictor);
        if (samples is null)
        {
            return null;
        }

        if (indexBpc > 0)
        {
            return ApplyPalette(samples, width.Value, height.Value, rowBytes, indexBpc, palette!, gray);
        }

        if (gray)
        {
            return new DecodedFlate(samples, width.Value, height.Value, true);
        }

        return new DecodedFlate(SwizzleRgbToBgr(samples), width.Value, height.Value, false);
    }

    // Task 2/3 placeholder: indirect /DecodeParms resolution comes later
    private static string DecodeParmsText(string dictText, Func<int, string?> refResolver) => dictText;

    private static bool TryDescribePixels(string dictText, Func<int, string?> refResolver, int width,
        out int components, out int indexBpc, out byte[]? palette, out bool gray)
    {
        components = 0;
        indexBpc = 0;
        palette = null;
        gray = false;
        var indexed = Regex.Match(dictText, @"/ColorSpace\s*\[\s*/Indexed\s+(\S+)\s+(\d+)", RegexOptions.Singleline);
        if (indexed.Success)
        {
            var baseName = indexed.Groups[1].Value;
            if (baseName != "/DeviceRGB" && baseName != "/DeviceGray")
            {
                return false;
            }

            gray = baseName == "/DeviceGray";
            if (!int.TryParse(indexed.Groups[2].Value, out var hival) || hival is < 0 or > 255)
            {
                return false;
            }

            var bits = MatchInt(dictText, "BitsPerComponent");
            if (bits is not (1 or 2 or 4 or 8))
            {
                return false;
            }

            var rest = dictText.Substring(indexed.Groups[2].Index + indexed.Groups[2].Length).TrimStart();
            byte[]? paletteBytes = null;
            if (rest.StartsWith("(", StringComparison.Ordinal))
            {
                if (!TryExtractBalancedLiteral(rest, 0, out var end))
                {
                    return false;
                }

                paletteBytes = DecodeLiteralString(rest.Substring(0, end + 1));
            }
            // NOTE: hex tails intentionally duplicated (direct vs indirect scans differ just enough to not share).
            else if (rest.StartsWith("<", StringComparison.Ordinal))
            {
                if (rest.StartsWith("<<", StringComparison.Ordinal))
                {
                    return false;
                }

                var end = rest.IndexOf('>');
                if (end < 0)
                {
                    return false;
                }

                paletteBytes = DecodeLiteralString(rest.Substring(0, end + 1));
            }
            else
            {
                var reference = Regex.Match(rest, @"^(\d+)\s+(\d+)\s+R");
                if (!reference.Success || !int.TryParse(reference.Groups[1].Value, out var refNumber))
                {
                    return false;
                }

                // NOTE: resolved against raw object text, not the parsed table (bare string objects have no dict)
                var resolved = refResolver(refNumber);
                if (resolved is null)
                {
                    return false;
                }

                var literalIndex = resolved.IndexOf('(');
                var hexIndex = -1;
                for (var i = 0; i < resolved.Length; i++)
                {
                    if (resolved[i] == '<')
                    {
                        if (i + 1 < resolved.Length && resolved[i + 1] == '<')
                        {
                            i++;
                            continue;
                        }

                        hexIndex = i;
                        break;
                    }
                }

                if (literalIndex >= 0 && (hexIndex < 0 || literalIndex < hexIndex))
                {
                    if (!TryExtractBalancedLiteral(resolved, literalIndex, out var end))
                    {
                        return false;
                    }

                    paletteBytes = DecodeLiteralString(resolved.Substring(literalIndex, end - literalIndex + 1));
                }
                else if (hexIndex >= 0)
                {
                    var end = resolved.IndexOf('>', hexIndex + 1);
                    if (end < 0)
                    {
                        return false;
                    }

                    paletteBytes = DecodeLiteralString(resolved.Substring(hexIndex, end - hexIndex + 1));
                }
                else
                {
                    return false;
                }
            }

            if (paletteBytes is null)
            {
                return false;
            }

            var channels = gray ? 1 : 3;
            if (paletteBytes.Length != (hival + 1) * channels)
            {
                return false;
            }

            indexBpc = bits.Value;
            palette = paletteBytes;
            return true;
        }

        if (MatchInt(dictText, "BitsPerComponent") != 8)
        {
            return false;
        }

        var colorSpace = Regex.Match(dictText, @"/ColorSpace\s*(/\S+)").Groups[1].Value;
        if (colorSpace == "/DeviceRGB")
        {
            components = 3;
            return true;
        }

        if (colorSpace == "/DeviceGray")
        {
            components = 1;
            gray = true;
            return true;
        }

        return false;
    }

    private static byte[]? ApplyPredictor(byte[] raw, int width, int height, int rowBytes, int components, int predictor)
    {
        if (predictor == 1)
        {
            return raw;
        }

        // TIFF predictor 2: each byte (past the first pixel of each row) is stored
        // as a delta from the same component of the previous pixel.
        var output = new byte[raw.Length];
        for (var row = 0; row < height; row++)
        {
            var offset = row * rowBytes;
            for (var i = 0; i < rowBytes; i++)
            {
                var prior = i >= components ? output[offset + i - components] : 0;
                output[offset + i] = (byte)(raw[offset + i] + prior);
            }
        }

        return output;
    }

    private static DecodedFlate? ApplyPalette(byte[] samples, int width, int height, int rowBytes, int indexBpc, byte[] palette, bool gray)
    {
        var channels = gray ? 1 : 3;
        var pixelBytes = (long)width * height * channels;
        if (pixelBytes > MaxDecompressedBytes)
        {
            return null;
        }

        var pixels = new byte[width * height * channels];
        var entries = palette.Length / channels;
        for (var row = 0; row < height; row++)
        {
            var bitPosition = row * rowBytes * 8;
            for (var col = 0; col < width; col++)
            {
                var index = 0;
                for (var b = 0; b < indexBpc; b++)
                {
                    var byteIndex = bitPosition / 8;
                    var bit = 7 - (bitPosition % 8);
                    index = (index << 1) | ((samples[byteIndex] >> bit) & 1);
                    bitPosition++;
                }

                if (index >= entries)
                {
                    return null;
                }

                var i = row * width + col;
                if (gray)
                {
                    pixels[i] = palette[index];
                }
                else
                {
                    pixels[i * 3] = palette[index * 3 + 2];
                    pixels[i * 3 + 1] = palette[index * 3 + 1];
                    pixels[i * 3 + 2] = palette[index * 3];
                }
            }
        }

        return new DecodedFlate(pixels, width, height, gray);
    }

    private static bool TryExtractBalancedLiteral(string text, int openParenIndex, out int closeIndex)
    {
        closeIndex = -1;
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                i++;
                if (i < text.Length && text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    closeIndex = i;
                    return true;
                }
            }
        }

        return false;
    }

    private static byte[] DecodeLiteralString(string token)
    {
        if (token.StartsWith("<", StringComparison.Ordinal))
        {
            var end = token.IndexOf('>');
            var inner = end >= 0 ? token.Substring(1, end - 1) : token.Substring(1);
            var cleaned = new StringBuilder(inner.Length);
            foreach (var c in inner)
            {
                if (!char.IsWhiteSpace(c))
                {
                    cleaned.Append(c);
                }
            }

            if (cleaned.Length % 2 == 1)
            {
                cleaned.Append('0');
            }

            var bytes = new byte[cleaned.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var pair = cleaned.ToString(i * 2, 2);
                if (!byte.TryParse(pair, System.Globalization.NumberStyles.HexNumber, null, out var value))
                {
                    return Array.Empty<byte>();
                }

                bytes[i] = value;
            }

            return bytes;
        }

        var body = token;
        if (body.StartsWith("(", StringComparison.Ordinal) && body.EndsWith(")", StringComparison.Ordinal) && body.Length >= 2)
        {
            body = body.Substring(1, body.Length - 2);
        }

        var output = new List<byte>(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c != '\\')
            {
                output.Add((byte)c);
                continue;
            }

            if (i + 1 >= body.Length)
            {
                break;
            }

            var next = body[++i];
            switch (next)
            {
                case 'n':
                    output.Add(10);
                    break;
                case 'r':
                    output.Add(13);
                    break;
                case 't':
                    output.Add(9);
                    break;
                case 'b':
                    output.Add(8);
                    break;
                case 'f':
                    output.Add(12);
                    break;
                case '(':
                    output.Add(40);
                    break;
                case ')':
                    output.Add(41);
                    break;
                case '\\':
                    output.Add(92);
                    break;
                case '\r':
                    if (i + 1 < body.Length && body[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\n':
                    break;
                case >= '0' and <= '7':
                    var value = next - '0';
                    var digits = 1;
                    while (digits < 3 && i + 1 < body.Length && body[i + 1] >= '0' && body[i + 1] <= '7')
                    {
                        value = value * 8 + (body[++i] - '0');
                        digits++;
                    }

                    output.Add((byte)value);
                    break;
                default:
                    output.Add((byte)next);
                    break;
            }
        }

        return output.ToArray();
    }

    private static byte[] SwizzleRgbToBgr(byte[] rgb)
    {
        var bgr = new byte[rgb.Length];
        for (var i = 0; i + 2 < rgb.Length; i += 3)
        {
            bgr[i] = rgb[i + 2];
            bgr[i + 1] = rgb[i + 1];
            bgr[i + 2] = rgb[i];
        }

        return bgr;
    }

    internal static byte[] SpliceImages(byte[] bytes, string text, Dictionary<int, QdfObject> objects,
        List<ImageReplacement> replacements)
    {
        using var spliced = new MemoryStream(bytes.Length);
        var cursor = 0;
        foreach (var replacement in replacements.OrderBy(r => objects[r.ObjectNumber].DictOffset))
        {
            var image = objects[replacement.ObjectNumber];
            var dictText = text.Substring(image.DictOffset, image.DictLength);
            var newDict = RewriteImageDict(dictText, replacement);

            spliced.Write(bytes, cursor, image.DictOffset - cursor);
            var newDictBytes = Latin1.GetBytes(newDict);
            spliced.Write(newDictBytes, 0, newDictBytes.Length);
            // Preserve the original ">> ... stream<EOL>" span verbatim ...
            spliced.Write(bytes, image.DictOffset + image.DictLength, image.StreamOffset - (image.DictOffset + image.DictLength));
            // ... but normalize the trailing EOL so Length stays exact.
            spliced.Write(replacement.JpegBytes, 0, replacement.JpegBytes.Length);
            var tail = Latin1.GetBytes("\nendstream");
            spliced.Write(tail, 0, tail.Length);
            cursor = image.StreamOffset + image.StreamLength;
            // Skip the original EOL before endstream.
            var skipped = SkipOriginalTail(text, cursor);
            cursor = skipped;
        }

        spliced.Write(bytes, cursor, bytes.Length - cursor);
        return spliced.ToArray();
    }

    // After the replaced stream data, the original file has "<EOL>endstream".
    // Returns the offset just past that EOL.
    private static int SkipOriginalTail(string text, int cursor)
    {
        var index = cursor;
        if (index < text.Length && text[index] == '\r')
        {
            index++;
        }

        if (index < text.Length && text[index] == '\n')
        {
            index++;
        }

        return index;
    }

    internal static string RewriteImageDict(string dictText, ImageReplacement replacement)
    {
        var rewritten = Regex.Replace(dictText, @"/Width\s+\d+", $"/Width {replacement.Width}");
        rewritten = Regex.Replace(rewritten, @"/Height\s+\d+", $"/Height {replacement.Height}");
        rewritten = LengthPattern.Replace(rewritten, $"/Length {replacement.JpegBytes.Length}");
        rewritten = FilterPattern.Replace(rewritten, "/Filter /DCTDecode");
        var colorSpace = replacement.Gray ? "/DeviceGray" : "/DeviceRGB";
        rewritten = ColorSpacePattern.Replace(rewritten, $"/ColorSpace {colorSpace}");
        rewritten = Regex.Replace(rewritten, @"/BitsPerComponent\s+\d+", "/BitsPerComponent 8");
        rewritten = DecodeParmsPattern.Replace(rewritten, string.Empty);
        return rewritten;
    }
}
