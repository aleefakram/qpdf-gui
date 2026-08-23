namespace QPdfDecryptor.Operations;

public interface IBusyPage
{
    bool IsBusy { get; }

    event EventHandler? BusyStateChanged;

    /// Cancels any in-flight work so closing the shell never orphans a qpdf process.
    void OnShellClosing();
}
