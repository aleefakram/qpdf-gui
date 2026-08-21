namespace QPdfDecryptor.Core;

public enum FileOutcome
{
    Decrypted,
    DecryptedNoPassword,
    NotEncrypted,
    Skipped,
    NoPasswordMatched,
    Failed
}
