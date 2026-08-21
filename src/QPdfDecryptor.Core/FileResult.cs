namespace QPdfDecryptor.Core;

public sealed record FileResult(
    string InputPath,
    string? OutputPath,
    FileOutcome Outcome,
    string? MatchedPassword,
    string Message,
    string Details);
