using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using QPdfDecryptor.Core;

namespace QPdfDecryptor;

public sealed class BulkFileRow : INotifyPropertyChanged
{
    private bool isRevealed;

    public BulkFileRow(FileResult result)
    {
        InputPath = result.InputPath;
        FileName = Path.GetFileName(result.InputPath);
        Message = result.Message;
        MatchedPassword = result.MatchedPassword;
        (Icon, IconBrush) = result.Outcome switch
        {
            FileOutcome.Decrypted or FileOutcome.DecryptedNoPassword
                => ("\uE73E", ResourceBrush("SuccessBrush")),
            FileOutcome.NotEncrypted or FileOutcome.Skipped
                => ("\uE946", ResourceBrush("MutedInkBrush")),
            _ => ("\uEA39", ResourceBrush("ErrorBrush"))
        };
    }

    public string InputPath { get; }
    public string FileName { get; }
    public string Message { get; }
    public string? MatchedPassword { get; }
    public bool HasPassword => !string.IsNullOrEmpty(MatchedPassword);
    public string Icon { get; }
    public Brush IconBrush { get; }

    public string PasswordDisplay =>
        isRevealed ? MatchedPassword ?? string.Empty : "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ToggleRevealed()
    {
        isRevealed = !isRevealed;
        OnPropertyChanged(nameof(PasswordDisplay));
    }

    private static Brush ResourceBrush(string key) => (Brush)Application.Current.FindResource(key);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
