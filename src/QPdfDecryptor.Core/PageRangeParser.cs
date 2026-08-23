namespace QPdfDecryptor.Core;

public static class PageRangeParser
{
    // Expands "1-3,5,z" (z = last page, reversed ranges allowed) into ordered 1-based
    // page numbers. Returns null when anything fails to parse or exceeds pageCount.
    public static List<int>? Parse(string? text, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(text) || pageCount < 1)
        {
            return null;
        }

        var pages = new List<int>();
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

            var resolved = parts.Select(part => ResolveBound(part.Trim(), pageCount)).ToList();
            if (resolved.Any(bound => bound is null))
            {
                return null;
            }

            var start = resolved[0]!.Value;
            var end = resolved.Count == 2 ? resolved[1]!.Value : start;
            var step = start <= end ? 1 : -1;
            for (var page = start; page != end + step; page += step)
            {
                pages.Add(page);
            }
        }

        return pages;
    }

    private static int? ResolveBound(string bound, int pageCount) => bound switch
    {
        "z" or "Z" => pageCount,
        _ when int.TryParse(bound, out var number) && number >= 1 && number <= pageCount => number,
        _ => null,
    };
}
