namespace QPdfDecryptor.Core;

public sealed record DecryptResult(bool Succeeded, bool HasWarnings, string Message, string Details)
{
    public static DecryptResult Success(bool hasWarnings, string details) => new(
        true,
        hasWarnings,
        hasWarnings ? "The PDF was decrypted with warnings." : "The decrypted PDF is ready.",
        details);

    public static DecryptResult Failure(string message, string details) =>
        new(false, false, message, details);
}
