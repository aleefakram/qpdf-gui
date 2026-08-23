namespace QPdfDecryptor.Core;

public enum BulkPhase
{
    ProbingPasswords,
    Decrypting,
    Copying
}

public sealed record BulkProgress(
    int CompletedFiles,
    int TotalFiles,
    string CurrentFile,
    BulkPhase Phase,
    int Attempt,
    int AttemptCount,
    int? DecryptPercent);
