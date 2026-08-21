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
    private readonly BulkDecryptService bulkDecryptService = new();
    private CancellationTokenSource? cancellationTokenSource;
    private string? inputPath;
    private string? qpdfPath;
    private bool isBusy;
    private IReadOnlyList<string> folderFiles = [];
    private IReadOnlyList<string> passwordList = [];
    private IReadOnlyList<FileResult> lastBulkResults = [];
    private bool userTunedOutputFolder;

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

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the folder with the encrypted PDFs"
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderPathInput.Text = dialog.FolderName;
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

    private void ChooseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to save the decrypted PDFs"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        OutputFolderInput.Text = dialog.FolderName;
        userTunedOutputFolder = true;
        ClearStatus();
    }

    private void ChoosePasswordList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the password list",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            passwordList = PasswordListParser.Parse(dialog.FileName);
            PasswordListPathInput.Text = dialog.FileName;
            PasswordListCount.Text = passwordList.Count == 1
                ? "1 password loaded"
                : $"{passwordList.Count} passwords loaded";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            passwordList = [];
            PasswordListPathInput.Text = string.Empty;
            PasswordListCount.Text = "The password list could not be read.";
        }

        UpdateDecryptButtonState();
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
        if (FolderModeRadio.IsChecked == true)
        {
            await RunBulkAsync();
            return;
        }

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

    private async Task RunBulkAsync()
    {
        if (!TryValidateBulk(out var validationMessage, out var invalidControl))
        {
            ShowError("Check the details", validationMessage);
            invalidControl?.Focus();
            return;
        }

        var candidates = SinglePasswordRadio.IsChecked == true
            ? new[] { GetPassword() }
            : passwordList;
        var request = new BulkDecryptRequest(
            qpdfPath!,
            folderFiles,
            candidates,
            OutputFolderInput.Text,
            SelectedConflictPolicy());
        var openFolderWhenFinished = OpenFolderCheckBox.IsChecked == true;

        SetBusy(true);
        ClearStatus();
        ResultsRegion.Visibility = Visibility.Collapsed;
        cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<BulkProgress>(value =>
        {
            DecryptProgress.Value = value.TotalFiles == 0
                ? 0
                : value.CompletedFiles * 100.0 / value.TotalFiles;
            ProgressText.Text = DescribeBulkProgress(value);
        });

        try
        {
            var result = await bulkDecryptService.DecryptAsync(request, progress, cancellationTokenSource.Token);

            ClearPassword();
            lastBulkResults = result.Files;
            ShowBulkResults(result.Files);

            if (openFolderWhenFinished && !TryOpenFolder(OutputFolderInput.Text, out var explorerError))
            {
                MessageBox.Show(
                    this,
                    explorerError,
                    "Could not open the folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            ClearPassword();
            ShowError("Decryption cancelled", "Finished files were kept and no partial files were left behind.");
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

    private bool TryValidateBulk(out string message, out IInputElement? invalidControl)
    {
        if (qpdfPath is null || !File.Exists(qpdfPath))
        {
            message = "The required PDF component is missing. Locate it above, or reinstall the finished app.";
            invalidControl = LocateQpdfButton;
            return false;
        }

        if (string.IsNullOrWhiteSpace(FolderPathInput.Text) || !Directory.Exists(FolderPathInput.Text))
        {
            message = "Choose the folder that contains the encrypted PDFs.";
            invalidControl = ChooseFolderButton;
            return false;
        }

        if (folderFiles.Count == 0)
        {
            message = "No PDF files were found in that folder.";
            invalidControl = ChooseFolderButton;
            return false;
        }

        if (SinglePasswordRadio.IsChecked != true)
        {
            if (string.IsNullOrWhiteSpace(PasswordListPathInput.Text) || !File.Exists(PasswordListPathInput.Text))
            {
                message = "Choose the text file that contains the passwords, one per line.";
                invalidControl = ChoosePasswordListButton;
                return false;
            }

            if (passwordList.Count == 0)
            {
                message = "The password list is empty. Enter one password per line.";
                invalidControl = ChoosePasswordListButton;
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(OutputFolderInput.Text))
        {
            message = "Choose where to save the decrypted PDFs.";
            invalidControl = ChooseOutputFolderButton;
            return false;
        }

        message = string.Empty;
        invalidControl = null;
        return true;
    }

    private static string DescribeBulkProgress(BulkProgress value)
    {
        var fileName = Path.GetFileName(value.CurrentFile);
        return value.Phase switch
        {
            BulkPhase.ProbingPasswords when value.AttemptCount > 1 =>
                $"Trying password {value.Attempt} of {value.AttemptCount}... {fileName}",
            BulkPhase.ProbingPasswords => $"Checking {fileName}...",
            BulkPhase.Decrypting when value.DecryptPercent is int percent && percent > 0 =>
                $"Decrypting {fileName}... {percent}%",
            BulkPhase.Decrypting => $"Decrypting {fileName}...",
            _ => $"Copying {fileName}..."
        };
    }

    private ConflictPolicy SelectedConflictPolicy() => ConflictPolicyInput.SelectedIndex switch
    {
        0 => ConflictPolicy.Overwrite,
        1 => ConflictPolicy.AutoRename,
        _ => ConflictPolicy.Skip
    };

    private void ShowBulkResults(IReadOnlyList<FileResult> files)
    {
        var decrypted = files.Count(file =>
            file.Outcome is FileOutcome.Decrypted or FileOutcome.DecryptedNoPassword);
        var copied = files.Count(file => file.Outcome == FileOutcome.NotEncrypted);
        var skipped = files.Count(file => file.Outcome == FileOutcome.Skipped);
        var failed = files.Count(file =>
            file.Outcome is FileOutcome.NoPasswordMatched or FileOutcome.Failed);

        ResultsSummary.Text =
            $"{decrypted} decrypted, {copied} copied, {skipped} skipped, {failed} failed";
        ResultsList.ItemsSource = files.Select(file => new BulkFileRow(file)).ToList();
        ExportReportButton.Visibility = Visibility.Visible;
        ResultsRegion.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ResultsRegion.BringIntoView();
            ResultsRegion.Focus();
        });
    }

    private void RevealResultPassword_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BulkFileRow row)
        {
            row.ToggleRevealed();
        }
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export the decryption report",
            Filter = "CSV files (*.csv)|*.csv",
            AddExtension = true,
            DefaultExt = ".csv",
            OverwritePrompt = true,
            FileName = "decryption-report.csv"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var lines = new List<string> { "Input,Outcome,Output,Message" };
        lines.AddRange(lastBulkResults.Select(file => string.Join(
            ",",
            CsvField(file.InputPath),
            CsvField(file.Outcome.ToString()),
            CsvField(file.OutputPath ?? string.Empty),
            CsvField(file.Message))));
        File.WriteAllLines(dialog.FileName, lines);
    }

    private static string CsvField(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private void ScanFolder()
    {
        lastBulkResults = [];
        ResultsRegion.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(FolderPathInput.Text) || !Directory.Exists(FolderPathInput.Text))
        {
            folderFiles = [];
            FolderFileCount.Text = string.Empty;
            UpdateDecryptButtonState();
            return;
        }

        try
        {
            var searchOption = IncludeSubfoldersCheckBox.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            folderFiles = Directory
                .EnumerateFiles(FolderPathInput.Text, "*.pdf", searchOption)
                .Where(path => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            FolderFileCount.Text = folderFiles.Count == 1
                ? "1 PDF file found"
                : $"{folderFiles.Count} PDF files found";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            folderFiles = [];
            FolderFileCount.Text = "The folder could not be read. Check its permissions and try again.";
        }

        UpdateDecryptButtonState();
    }

    private void InputMode_Changed(object sender, RoutedEventArgs e)
    {
        if (SingleFileSection is null)
        {
            return;
        }

        var singleMode = SingleFileModeRadio.IsChecked == true;
        SingleFileSection.Visibility = singleMode ? Visibility.Visible : Visibility.Collapsed;
        FolderInputSection.Visibility = singleMode ? Visibility.Collapsed : Visibility.Visible;
        SingleOutputSection.Visibility = singleMode ? Visibility.Visible : Visibility.Collapsed;
        FolderOutputSection.Visibility = singleMode ? Visibility.Collapsed : Visibility.Visible;
        ChooseInputButton.Visibility = singleMode ? Visibility.Visible : Visibility.Collapsed;
        InputFileName.Text = singleMode ? "Drop a PDF here" : "Drop a folder here";
        InputFilePath.Text = singleMode
            ? "or choose a file from this computer"
            : "or choose a folder from this computer";
        ClearStatus();
        UpdateDecryptButtonState();
    }

    private void PasswordMode_Changed(object sender, RoutedEventArgs e)
    {
        if (SinglePasswordSection is null)
        {
            return;
        }

        var singleMode = SinglePasswordRadio.IsChecked == true;
        SinglePasswordSection.Visibility = singleMode ? Visibility.Visible : Visibility.Collapsed;
        ListPasswordSection.Visibility = singleMode ? Visibility.Collapsed : Visibility.Visible;
        ClearStatus();
        UpdateDecryptButtonState();
    }

    private void IncludeSubfolders_Changed(object sender, RoutedEventArgs e)
    {
        if (FolderPathInput is null)
        {
            return;
        }

        ScanFolder();
    }

    private void FolderPathInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!userTunedOutputFolder)
        {
            OutputFolderInput.Text = string.IsNullOrWhiteSpace(FolderPathInput.Text)
                ? string.Empty
                : Path.Combine(FolderPathInput.Text, "decrypted");
        }

        ScanFolder();
    }

    private void PasswordListPathInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateDecryptButtonState();

    private void OutputFolderInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateDecryptButtonState();

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
        var acceptable = FolderModeRadio.IsChecked == true
            ? HasSingleEntry(e.Data)
            : HasSinglePdf(e.Data);
        e.Effects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
        DropTarget.BorderBrush = acceptable ? ActiveDropBorder : DefaultDropBorder;
        e.Handled = true;
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e) =>
        DropTarget.BorderBrush = DefaultDropBorder;

    private void DropTarget_Drop(object sender, DragEventArgs e)
    {
        DropTarget.BorderBrush = DefaultDropBorder;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
        {
            return;
        }

        if (FolderModeRadio.IsChecked == true)
        {
            if (Directory.Exists(files[0]))
            {
                FolderPathInput.Text = files[0];
            }

            return;
        }

        if (HasSinglePdf(e.Data))
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
            message = "The password cannot contain a line break. Use a password list for multi-line passwords.";
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
        SingleFileModeRadio.IsEnabled = !isBusy;
        FolderModeRadio.IsEnabled = !isBusy;
        ChooseFolderButton.IsEnabled = !isBusy;
        IncludeSubfoldersCheckBox.IsEnabled = !isBusy;
        SinglePasswordRadio.IsEnabled = !isBusy;
        ListPasswordRadio.IsEnabled = !isBusy;
        PasswordListPathInput.IsEnabled = !isBusy;
        ChoosePasswordListButton.IsEnabled = !isBusy;
        OutputFolderInput.IsEnabled = !isBusy;
        ChooseOutputFolderButton.IsEnabled = !isBusy;
        ConflictPolicyInput.IsEnabled = !isBusy;
        ExportReportButton.IsEnabled = !isBusy;
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
        var folderMode = FolderModeRadio is not null && FolderModeRadio.IsChecked == true;
        var inputReady = folderMode
            ? folderFiles.Count > 0
            : inputPath is not null && File.Exists(inputPath);
        var outputReady = folderMode
            ? !string.IsNullOrWhiteSpace(OutputFolderInput?.Text)
            : !string.IsNullOrWhiteSpace(OutputPathInput.Text);

        DecryptButton.IsEnabled = !isBusy &&
            qpdfPath is not null && File.Exists(qpdfPath) &&
            inputReady && outputReady;

        DecryptButton.Content = folderMode
            ? (folderFiles.Count > 0 ? $"Decrypt {folderFiles.Count} _PDFs" : "Decrypt _PDFs")
            : "_Decrypt PDF";
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

    private static bool HasSingleEntry(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) &&
        data.GetData(DataFormats.FileDrop) is string[] { Length: 1 };

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

    private static bool TryOpenFolder(string folderPath, out string error)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { folderPath },
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
