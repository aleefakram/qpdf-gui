using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace QPdfDecryptor;

public partial class MainWindow : Window
{
    private OperationInfo currentInfo = OperationCatalog.Find("decrypt");

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

        if (!ReferenceEquals(info, currentInfo) && PageHost.Content is Operations.DecryptPage { IsBusy: true })
        {
            NavList.SelectedItem = currentInfo;
            return;
        }

        currentInfo = info;
        NavigateTo(info);
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
        (PageHost.Content as Operations.DecryptPage)?.OnShellClosing();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        (PageHost.Content as Operations.DecryptPage)?.HandleShellPreviewKey(e);
    }
}
