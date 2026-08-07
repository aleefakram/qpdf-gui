namespace QPdfDecryptor.Core;

public sealed record DecryptRequest(
    string QpdfPath,
    string InputPath,
    string OutputPath,
    string Password);
