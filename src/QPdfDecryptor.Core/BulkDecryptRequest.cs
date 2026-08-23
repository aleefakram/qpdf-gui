namespace QPdfDecryptor.Core;

public sealed record BulkDecryptRequest(
    string QpdfPath,
    IReadOnlyList<string> InputPaths,
    IReadOnlyList<string> PasswordCandidates,
    string OutputDirectory,
    ConflictPolicy ConflictPolicy,
    string? InputRoot = null);
