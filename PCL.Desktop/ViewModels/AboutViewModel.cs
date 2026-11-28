using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;

namespace PCL.Desktop.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private const string PurchaseUrl =
        "https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj";

    private const string SponsorUrl = "https://afdian.com/a/LTCat";
    private const string UpstreamUrl = "https://github.com/Meloong-Git/PCL";
    private const string CommunityUrl = "https://github.com/PCL-Community/PCL-CE";
    private const string HelpUrl = "https://www.pclc.cc/";
    private const string FeedbackUrl = "https://github.com/PCL-Community/PCL-CE/issues/new/choose";

    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;

    public AboutViewModel(LauncherServices services)
    {
        _services = services;
        _strings = services.Localization;
    }

    public string Title => _strings["app.title"];

    public string Version => _strings["about.version"];

    public string Attribution => _strings["about.attribution"];

    public string LicenseSectionTitle => _strings["about.license.title"];

    public string LicenseNotice => _strings["about.license.notice"];

    public string CleanRoomSectionTitle => _strings["about.clean.title"];

    public string CleanRoomNotice => _strings["about.clean.notice"];

    public string SupportSectionTitle => _strings["about.support.title"];

    public string SupportDescription => _strings["about.support.description"];

    public string PurchaseLabel => _strings["about.action.purchase"];

    public string SponsorLabel => _strings["about.action.sponsor"];

    public string UpstreamLabel => _strings["about.action.upstream"];

    public string CommunityLabel => _strings["about.action.community"];

    public string HelpSectionTitle => _strings["about.help.title"];

    public string HelpDescription => _strings["about.help.description"];

    public string DocsLabel => _strings["about.action.docs"];

    public string FeedbackLabel => _strings["about.action.feedback"];

    [ObservableProperty]
    private string status = "";

    [RelayCommand]
    private Task OpenPurchaseAsync() => OpenAsync(PurchaseUrl);

    [RelayCommand]
    private Task OpenSponsorAsync() => OpenAsync(SponsorUrl);

    [RelayCommand]
    private Task OpenUpstreamAsync() => OpenAsync(UpstreamUrl);

    [RelayCommand]
    private Task OpenCommunityAsync() => OpenAsync(CommunityUrl);

    [RelayCommand]
    private Task OpenHelpAsync() => OpenAsync(HelpUrl);

    [RelayCommand]
    private Task OpenFeedbackAsync() => OpenAsync(FeedbackUrl);

    private async Task OpenAsync(string url)
    {
        try
        {
            await _services.UrlOpener.OpenAsync(new Uri(url));
            Status = "";
        }
        catch (Exception ex)
        {
            Status = _strings["about.error.open"] + ex.Message;
        }
    }
}
