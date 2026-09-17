using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace QPdfDecryptor;

public partial class App : Application
{
    // Last-resort handler: async-void page handlers must not silently kill the app.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDF Ninja", "error.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"{DateTime.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        MessageBox.Show(
            $"Something went wrong: {e.Exception.Message}{Environment.NewLine}{Environment.NewLine}Details were saved to {logPath}",
            "PDF Ninja", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
