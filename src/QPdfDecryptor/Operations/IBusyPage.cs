namespace QPdfDecryptor.Operations;

public interface IBusyPage
{
    bool IsBusy { get; }

    event EventHandler? BusyStateChanged;
}
