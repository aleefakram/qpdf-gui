namespace QPdfDecryptor.Core;

// An operation that reads PDF file(s) and writes output file(s). BuildArguments
// receives the temporary output path so the executor controls where writing happens.
public interface IQpdfFileOperation
{
    string QpdfPath { get; }
    string OutputPath { get; }

    /// Exception-based validation of user-settable fields, called before any process spawns.
    void Validate();

    /// The complete qpdf argument list, ending "... <tempOutput>".
    IReadOnlyList<string> BuildArguments(string temporaryOutputPath);

    /// Primary input probed for encryption before the operation runs; empty means no probe.
    string ProbeInputPath { get; }
}
