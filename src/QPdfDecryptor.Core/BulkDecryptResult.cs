namespace QPdfDecryptor.Core;

public sealed record BulkDecryptResult(IReadOnlyList<FileResult> Files);
