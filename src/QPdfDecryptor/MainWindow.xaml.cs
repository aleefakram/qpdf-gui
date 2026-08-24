using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace QPdfDecryptor;

public partial class MainWindow : Window
{
    // Null until the first navigation; pre-seeding it with Decrypt would make the
    // constructor's selection look like a "no change" and skip rendering the page.
    private OperationInfo? currentInfo;

    public MainWindow()
    {
        InitializeComponent();
        NavList.ItemsSource = OperationCatalog.All;
        NavList.Items.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OperationInfo.Group)));
        NavList.SelectedItem = OperationCatalog.Find("decrypt");
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not OperationInfo info)
        {
            return;
        }

        if (!ReferenceEquals(info, currentInfo))
        {
            if (PageHost.Content is Operations.IBusyPage { IsBusy: true })
            {
                NavList.SelectedItem = currentInfo;
                return;
            }

            currentInfo = info;
            NavigateTo(info);
        }
    }

    private void NavigateTo(OperationInfo info)
    {
        PageHost.Content = info.Id switch
        {
            "decrypt" => new Operations.DecryptPage(),
            "merge" => new Operations.MergePage(),
            "split" => new Operations.SplitPage(),
            "organize" => new Operations.OrganizePage(),
            "rotate" => new Operations.RotatePage(),
            "compress" => new Operations.CompressPage(),
            "watermark" => new Operations.WatermarkPage(),
            _ => null,
        };
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        (PageHost.Content as Operations.IBusyPage)?.OnShellClosing();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        (PageHost.Content as Operations.DecryptPage)?.HandleShellPreviewKey(e);
    }
}
