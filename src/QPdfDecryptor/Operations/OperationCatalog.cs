namespace QPdfDecryptor;

public sealed record OperationInfo(
    string Id,
    string Group,     // Secure | Arrange | Enhance
    string Title,
    string Subtitle,
    string Glyph);    // Segoe Fluent Icons codepoint

public static class OperationCatalog
{
    public static readonly IReadOnlyList<OperationInfo> All =
    [
        new("decrypt",   "Secure",  "Decrypt",   "remove password",   "\uE1F6"),
        new("merge",     "Arrange", "Merge",     "combine PDFs",      "\uE8E5"),
        new("split",     "Arrange", "Split",     "break apart",       "\uF1D8"),
        new("organize",  "Arrange", "Organize",  "reorder · delete",  "\uE8FD"),
        new("rotate",    "Arrange", "Rotate",    "turn pages",        "\uE7AD"),
        new("compress",  "Enhance", "Compress",  "shrink size",       "\uE9D2"),
        new("watermark", "Enhance", "Watermark", "stamp pages",       "\uEC15"),
    ];

    public static OperationInfo Find(string id) =>
        All.First(operation => operation.Id == id);
}
