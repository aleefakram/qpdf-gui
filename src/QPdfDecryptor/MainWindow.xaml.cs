using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QPdfDecryptor.Core;

namespace QPdfDecryptor;

public partial class MainWindow : Window
{
    private static readonly Brush DefaultDropBorder = new SolidColorBrush(Color.FromRgb(212, 218, 221));
    private static readonly Brush ActiveDropBorder = new SolidColorBrush(Color.FromRgb(22, 123, 130));
    private readonly QpdfDecryptService decryptService = new();
    private CancellationTokenSource? cancellationTokenSource;
    private string? inputPath;
    private string? qpdfPath;
    private bool isBusy;

    public MainWindow()
    {
        InitializeComponent();
        qpdfPath = FindQpdfExecutable();
        UpdateSetupState();
        UpdateDecryptButtonState();
    }

    private void ChooseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an encrypted PDF",
            Filter = "PDF files (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            SetInputFile(dialog.FileName);
        }
    }

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var suggestedPath = OutputPathInput.Text;
        var dialog = new SaveFileDialog
        {
            Title = "Save decrypted PDF",
            Filter = "PDF files (*.pdf)|*.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true,
            FileName = Path.GetFileName(suggestedPath),
            InitialDirectory = Path.GetDirectoryName(suggestedPath)
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputPathInput.Text = dialog.FileName;
            ClearStatus();
        }
    }

    private void LocateQpdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate the qpdf component",
            Filter = "qpdf executable (qpdf.exe)|qpdf.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            qpdfPath = dialog.FileName;
            UpdateSetupState();
            UpdateDecryptButtonState();
        }
    }

    private async void Decrypt_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidate(out var validationMessage, out var invalidControl))
        {
            ShowError("Check the details", validationMessage);
            invalidControl?.Focus();
            return;
        }

        if (File.Exists(OutputPathInput.Text))
        {
            var choice = MessageBox.Show(
                this,
                "A file already exists at the selected location. Replace it?",
                "Replace existing PDF",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var request = new DecryptRequest(
            qpdfPath!, inputPath!, OutputPathInput.Text, GetPassword());
        var openFolderWhenFinished = OpenFolderCheckBox.IsChecked == true;

        SetBusy(true);
        ClearStatus();
        cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<int>(value =>
        {
            DecryptProgress.Value = value;
            ProgressText.Text = value > 0 ? $"Decrypting PDF... {value}%" : "Preparing PDF...";
        });

        try
        {
            var result = await decryptService.DecryptAsync(request, progress, cancellationTokenSource.Token);

            ClearPassword();
            if (result.Succeeded)
            {
                var statusMessage = request.OutputPath;
                var details = result.Details;
                var hasWarnings = result.HasWarnings;
                if (openFolderWhenFinished && !TryOpenOutputFolder(request.OutputPath, out var explorerError))
                {
                    statusMessage += $"{Environment.NewLine}The folder could not be opened automatically.";
                    details = string.Join(
                        Environment.NewLine,
                        new[] { details, explorerError }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    hasWarnings = true;
                }
                ShowSuccess(result.Message, statusMessage, details, hasWarnings);
            }
            else
            {
                ShowError(result.Message, "No output file was created.", result.Details);
            }
        }
        catch (OperationCanceledException)
        {
            ClearPassword();
            ShowError("Decryption cancelled", "No output file was created.");
        }
        catch (ArgumentException exception)
        {
            ClearPassword();
            ShowError("Check the details", exception.Message);
        }
        finally
        {
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => cancellationTokenSource?.Cancel();

    private void RevealPassword_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Visibility == Visibility.Visible)
        {
            var password = PasswordInput.Password;
            PasswordInput.Clear();
            VisiblePasswordInput.Text = password;
            PasswordInput.Visibility = Visibility.Collapsed;
            VisiblePasswordInput.Visibility = Visibility.Visible;
            RevealPasswordButton.ToolTip = "Hide password";
            AutomationProperties.SetName(RevealPasswordButton, "Hide password");
            VisiblePasswordInput.Focus();
            VisiblePasswordInput.CaretIndex = VisiblePasswordInput.Text.Length;
        }
        else
        {
            var password = VisiblePasswordInput.Text;
            VisiblePasswordInput.Clear();
            PasswordInput.Password = password;
            VisiblePasswordInput.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            RevealPasswordButton.ToolTip = "Show password";
            AutomationProperties.SetName(RevealPasswordButton, "Show password");
            PasswordInput.Focus();
        }
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) => ClearStatus();

    private void VisiblePasswordInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ClearStatus();

    private void DropTarget_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = HasSinglePdf(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        DropTarget.BorderBrush = e.Effects == DragDropEffects.Copy ? ActiveDropBorder : DefaultDropBorder;
        e.Handled = true;
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e) =>
        DropTarget.BorderBrush = DefaultDropBorder;

    private void DropTarget_Drop(object sender, DragEventArgs e)
    {
        DropTarget.BorderBrush = DefaultDropBorder;
        if (HasSinglePdf(e.Data) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            SetInputFile(files[0]);
        }
    }

    private void SetInputFile(string path)
    {
        inputPath = path;
        InputFileName.Text = Path.GetFileName(path);
        InputFilePath.Text = path;
        OutputPathInput.Text = SuggestOutputPath(path);
        ClearStatus();
        PasswordInput.Focus();
    }

    private bool TryValidate(out string message, out IInputElement? invalidControl)
    {
        if (qpdfPath is null || !File.Exists(qpdfPath))
        {
            message = "The required PDF component is missing. Locate it above, or reinstall the finished app.";
            invalidControl = LocateQpdfButton;
            return false;
        }
        if (inputPath is null || !File.Exists(inputPath))
        {
            message = "Choose the encrypted PDF you want to decrypt.";
            invalidControl = ChooseInputButton;
            return false;
        }
        if (string.IsNullOrWhiteSpace(OutputPathInput.Text))
        {
            message = "Choose where to save the decrypted PDF.";
            invalidControl = ChooseOutputButton;
            return false;
        }
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(OutputPathInput.Text), StringComparison.OrdinalIgnoreCase))
        {
            message = "Save the decrypted copy with a different name or in a different folder.";
            invalidControl = ChooseOutputButton;
            return false;
        }
        if (GetPassword().Contains('\r') || GetPassword().Contains('\n'))
        {
            message = "The password cannot contain a line break.";
            invalidControl = PasswordInput.Visibility == Visibility.Visible
                ? PasswordInput
                : VisiblePasswordInput;
            return false;
        }

        message = string.Empty;
        invalidControl = null;
        return true;
    }

    private void SetBusy(bool isBusy)
    {
        this.isBusy = isBusy;
        CancelButton.Visibility = isBusy ? Visibility.Visible : Visibility.Hidden;
        ProgressRegion.Visibility = isBusy ? Visibility.Visible : Visibility.Hidden;
        DropTarget.IsEnabled = !isBusy;
        ChooseInputButton.IsEnabled = !isBusy;
        PasswordInput.IsEnabled = !isBusy;
        VisiblePasswordInput.IsEnabled = !isBusy;
        RevealPasswordButton.IsEnabled = !isBusy;
        OutputPathInput.IsEnabled = !isBusy;
        ChooseOutputButton.IsEnabled = !isBusy;
        OpenFolderCheckBox.IsEnabled = !isBusy;
        LocateQpdfButton.IsEnabled = !isBusy;
        DecryptProgress.Value = 0;
        UpdateDecryptButtonState();
    }

    private void ShowSuccess(string title, string outputPath, string details, bool hasWarnings)
    {
        StatusRegion.Background = (Brush)FindResource(hasWarnings ? "WarningSurfaceBrush" : "SuccessSurfaceBrush");
        StatusTitle.Foreground = (Brush)FindResource(hasWarnings ? "WarningBrush" : "SuccessBrush");
        StatusIcon.Foreground = StatusTitle.Foreground;
        StatusIcon.Text = hasWarnings ? "\uE7BA" : "\uE73E";
        StatusTitle.Text = title;
        StatusMessage.Text = outputPath;
        SetDetails(details, hasWarnings);
        RevealStatus();
    }

    private void ShowError(string title, string message, string details = "")
    {
        StatusRegion.Background = (Brush)FindResource("ErrorSurfaceBrush");
        StatusTitle.Foreground = (Brush)FindResource("ErrorBrush");
        StatusIcon.Foreground = StatusTitle.Foreground;
        StatusIcon.Text = "\uEA39";
        StatusTitle.Text = title;
        StatusMessage.Text = message;
        SetDetails(details, !string.IsNullOrWhiteSpace(details));
        RevealStatus();
    }

    private void SetDetails(string details, bool show)
    {
        DetailsText.Text = details;
        DetailsExpander.IsExpanded = false;
        DetailsExpander.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearStatus() => StatusRegion.Visibility = Visibility.Hidden;

    private void RevealStatus()
    {
        StatusRegion.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            StatusRegion.BringIntoView();
            StatusRegion.Focus();
        });
    }

    private void UpdateSetupState() =>
        SetupRegion.Visibility = qpdfPath is not null && File.Exists(qpdfPath)
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void UpdateDecryptButtonState()
    {
        DecryptButton.IsEnabled = !isBusy &&
            qpdfPath is not null && File.Exists(qpdfPath) &&
            inputPath is not null && File.Exists(inputPath) &&
            !string.IsNullOrWhiteSpace(OutputPathInput.Text);
    }

    private void OutputPathInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateDecryptButtonState();

    private string GetPassword() => PasswordInput.Visibility == Visibility.Visible
        ? PasswordInput.Password
        : VisiblePasswordInput.Text;

    private void ClearPassword()
    {
        PasswordInput.Clear();
        VisiblePasswordInput.Clear();
    }

    private static bool HasSinglePdf(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) &&
        data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
        Path.GetExtension(files[0]).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    private static string SuggestOutputPath(string input) =>
        Path.Combine(
            Path.GetDirectoryName(input) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(input)} - decrypted.pdf");

    private static string? FindQpdfExecutable()
    {
        var environmentPath = Environment.GetEnvironmentVariable("QPDF_EXECUTABLE");
        var candidates = new[]
        {
            environmentPath,
            Path.Combine(AppContext.BaseDirectory, "qpdf.exe"),
            Path.Combine(AppContext.BaseDirectory, "Native", "qpdf.exe")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static bool TryOpenOutputFolder(string outputPath, out string error)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { $"/select,{outputPath}" },
                UseShellExecute = true
            });
            if (process is null)
            {
                error = "Windows Explorer did not start.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = $"Windows Explorer could not be started: {exception.Message}";
            return false;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        cancellationTokenSource?.Cancel();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && isBusy)
        {
            cancellationTokenSource?.Cancel();
            e.Handled = true;
        }
    }
}
