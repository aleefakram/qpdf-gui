namespace QPdfDecryptor.Core;

public sealed record MergeRequest(
    string QpdfPath,
    IReadOnlyList<string> InputPaths,
    string OutputPath) : IQpdfFileOperation
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QpdfPath);
        if (InputPaths.Count == 0)
        {
            throw new ArgumentException("Merge requires at least one input PDF.", nameof(InputPaths));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);
    }

    public IReadOnlyList<string> BuildArguments(string temporaryOutputPath)
    {
        var arguments = new List<string> { "--empty", "--pages" };
        foreach (var input in InputPaths)
        {
            arguments.Add(input);
            arguments.Add("1-z");
        }

        arguments.Add("--");
        arguments.Add(temporaryOutputPath);
        return arguments;
    }
}

public sealed record SplitRequest(
    string QpdfPath,
    string InputPath,
    int PagesPerFile,
    string OutputBasePath) : IQpdfFileOperation
{
    // Split writes many files named <base>-<first>-<last>.pdf next to the temp path;
    // the interface property exists for temp-path placement only.
    string IQpdfFileOperation.OutputPath => OutputBasePath;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QpdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputBasePath);
        if (PagesPerFile < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PagesPerFile));
        }
    }

    public IReadOnlyList<string> BuildArguments(string temporaryOutputPath) =>
    [
        $"--split-pages={PagesPerFile}",
        InputPath,
        temporaryOutputPath,
    ];
}

public sealed record RotateRequest(
    string QpdfPath,
    string InputPath,
    int RelativeAngleDegrees, // 90, -90 or 180
    string? PageRange,        // null or "" means every page
    string OutputPath) : IQpdfFileOperation
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QpdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);
        if (RelativeAngleDegrees is not (90 or -90 or 180))
        {
            throw new ArgumentOutOfRangeException(nameof(RelativeAngleDegrees));
        }
    }

    public IReadOnlyList<string> BuildArguments(string temporaryOutputPath)
    {
        var rotation = RelativeAngleDegrees > 0
            ? $"+{RelativeAngleDegrees}"
            : RelativeAngleDegrees.ToString();
        if (!string.IsNullOrWhiteSpace(PageRange))
        {
            rotation += $":{PageRange}";
        }

        return ["--rotate=" + rotation, InputPath, temporaryOutputPath];
    }
}

public sealed record OrganizeRequest(
    string QpdfPath,
    string InputPath,
    string PageRanges, // "1-3,5,z" — order, duplicates and omissions are meaningful
    string OutputPath) : IQpdfFileOperation
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QpdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);
        if (!PageRangeParser.IsValid(PageRanges, int.MaxValue))
        {
            throw new ArgumentException("Page ranges are invalid.", nameof(PageRanges));
        }
    }

    public IReadOnlyList<string> BuildArguments(string temporaryOutputPath) =>
    [
        InputPath,
        "--pages",
        ".",
        PageRanges.Replace(" ", string.Empty),
        "--",
        temporaryOutputPath,
    ];
}

public sealed record CompressRequest(
    string QpdfPath,
    string InputPath,
    bool LinearizeForWeb,
    string OutputPath) : IQpdfFileOperation
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QpdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);
    }

    public IReadOnlyList<string> BuildArguments(string temporaryOutputPath)
    {
        var arguments = new List<string>
        {
            "--compress-streams=y",
            "--recompress-flate",
            "--object-streams=generate",
            "--decode-level=generalized",
        };
        if (LinearizeForWeb)
        {
            arguments.Add("--linearize");
        }

        arguments.Add(InputPath);
        arguments.Add(temporaryOutputPath);
        return arguments;
    }
}

public sealed record WatermarkRequest(
    string QpdfPath,
    string InputPath,
    string WatermarkPdfPath,
    bool BehindContent, // false = overlay (in front), true = underlay (behind)
    string OutputPath) : IQpdfFileOperation
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QpdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(WatermarkPdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);
    }

    public IReadOnlyList<string> BuildArguments(string temporaryOutputPath) =>
    [
        InputPath,
        BehindContent ? "--underlay" : "--overlay",
        WatermarkPdfPath,
        "--repeat=1-z",
        "--",
        temporaryOutputPath,
    ];
}
