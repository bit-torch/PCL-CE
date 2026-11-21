using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;

namespace PCL.Desktop.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private const string PurchaseUrl =
        "https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj";

    private const string SponsorUrl = "https://afdian.com/a/LTCat";
    private const string UpstreamUrl = "https://github.com/Meloong-Git/PCL";
    private const string CommunityUrl = "https://github.com/PCL-Community/PCL-CE";

    private readonly LauncherServices _services;

    public AboutViewModel(LauncherServices services)
    {
        _services = services;
    }

    public string Title => "PCL Community Edition";

    public string Version => "新内核预览 · Avalonia";

    public string Attribution =>
        "原 PCL 作者：龙腾猫跃。当前仓库派生自 PCL，并包含 PCL Community 的社区工作。";

    public string LicenseNotice =>
        "在 PCL 代码与资源完全替换前，本项目继续遵守《PCL 分发有限许可》：保留 PCL 名称前缀、" +
        "关于页署名、开源、正版购买劝导与 Windows 蓝色主色相。资源替换完成后才会调整许可声明。";

    public string CleanRoomNotice =>
        "新内核 PCL.Kernel 为独立实现，不复用 PCL 的签名、更新、赞助、统计与本地化资源。";

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

    private async Task OpenAsync(string url)
    {
        try
        {
            await _services.UrlOpener.OpenAsync(new Uri(url));
            Status = "";
        }
        catch (Exception ex)
        {
            Status = "无法打开链接：" + ex.Message;
        }
    }
}
