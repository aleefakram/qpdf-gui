namespace QPdfDecryptor.Core;

public static class PageRangeParser
{
    // Expands "1-3,5,z" (z = last page, reversed ranges allowed) into ordered 1-based
    // page numbers. Returns null when anything fails to parse or exceeds pageCount.
    public static List<int>? Parse(string? text, int pageCount)
    {
        var ranges = ResolveRanges(text, pageCount);
        if (ranges is null)
        {
            return null;
        }

        var pages = new List<int>();
        foreach (var (start, end) in ranges)
        {
            var step = start <= end ? 1 : -1;
            for (var page = start; page != end + step; page += step)
            {
                pages.Add(page);
            }
        }

        return pages;
    }

    // Grammar check without expansion: true when Parse(text, pageCount) would succeed.
    // Safe for huge counts ("1-z" against int.MaxValue) because only bounds are resolved.
    public static bool IsValid(string? text, int pageCount) =>
        ResolveRanges(text, pageCount) is not null;

    private static List<(int Start, int End)>? ResolveRanges(string? text, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(text) || pageCount < 1)
        {
            return null;
        }

        var ranges = new List<(int Start, int End)>();
        foreach (var rawToken in text.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                return null;
            }

            var parts = token.Split('-');
            if (parts.Length > 2)
            {
                return null;
            }

            var resolved = parts.Select(part => ResolveBound(part.Trim(), pageCount)).ToArray();
            if (resolved.Any(bound => bound is null))
            {
                return null;
            }

            var start = resolved[0]!.Value;
            var end = resolved.Length == 2 ? resolved[1]!.Value : start;
            ranges.Add((start, end));
        }

        return ranges;
    }

    private static int? ResolveBound(string bound, int pageCount) => bound switch
    {
        "z" or "Z" => pageCount,
        _ when int.TryParse(bound, out var number) && number >= 1 && number <= pageCount => number,
        _ => null,
    };
}
