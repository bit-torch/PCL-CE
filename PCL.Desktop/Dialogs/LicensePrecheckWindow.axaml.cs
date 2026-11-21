using Avalonia.Controls;
using Avalonia.Interactivity;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;

namespace PCL.Desktop.Dialogs;

public partial class LicensePrecheckWindow : Window
{
    private const string PurchaseUrl =
        "https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj";

    private const string SponsorUrl = "https://afdian.com/a/LTCat";

    public LicensePrecheckWindow() : this(new LocalizationService())
    {
    }

    public LicensePrecheckWindow(LocalizationService strings)
    {
        InitializeComponent();

        Title = strings["license.title"];
        HeadingText.Text = strings["license.heading"];
        IntroText.Text = strings["license.intro"];
        PurchaseHintText.Text = strings["license.purchase.hint"];
        IgnoreHintText.Text = strings["license.purchase.ignore"];
        PurchaseButton.Content = strings["license.action.purchase"];
        SponsorButton.Content = strings["license.action.sponsor"];
        LaterButton.Content = strings["license.action.later"];
        AcceptButton.Content = strings["license.action.accept"];
    }

    private void OnPurchaseClick(object? sender, RoutedEventArgs e) => Open(PurchaseUrl);

    private void OnSponsorClick(object? sender, RoutedEventArgs e) => Open(SponsorUrl);

    private void OnAcceptClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnDeclineClick(object? sender, RoutedEventArgs e) => Close(false);

    private static async void Open(string url)
    {
        try
        {
            await new SystemUrlOpener().OpenAsync(new Uri(url));
        }
        catch
        {
            // 打开浏览器失败不应阻塞启动流程。
        }
    }
}
