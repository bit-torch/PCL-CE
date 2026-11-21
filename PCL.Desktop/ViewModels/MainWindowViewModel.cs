using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;

namespace PCL.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly string[] PageTitles = ["总览", "安装", "启动", "设置", "关于"];

    private static readonly string[] PageSubtitles =
    [
        "干净内核与 Avalonia 新界面的起点",
        "获取 Mojang 官方版本并安装",
        "选择版本、Java 与账户后启动游戏",
        "管理游戏目录、Java 与内存",
        "许可证、署名与项目状态"
    ];

    public MainWindowViewModel(LauncherServices services, Func<Task<bool>> licenseGate)
    {
        NavItems = NavItem.CreateDefault();

        Launch = new LaunchViewModel(services, licenseGate);
        Install = new InstallViewModel(services, Launch.RefreshList);
        SettingsPage = new SettingsViewModel(services);
        About = new AboutViewModel(services);
        Home = new HomeViewModel(services, NavigateTo);

        currentPage = Home;
        currentTitle = PageTitles[0];
        currentSubtitle = PageSubtitles[0];
    }

    public IReadOnlyList<NavItem> NavItems { get; }

    public HomeViewModel Home { get; }

    public InstallViewModel Install { get; }

    public LaunchViewModel Launch { get; }

    public SettingsViewModel SettingsPage { get; }

    public AboutViewModel About { get; }

    [ObservableProperty]
    private int selectedNavIndex;

    [ObservableProperty]
    private ObservableObject currentPage;

    [ObservableProperty]
    private string currentTitle;

    [ObservableProperty]
    private string currentSubtitle;

    [ObservableProperty]
    private string statusText = "干净内核 · Avalonia";

    public void NavigateTo(string key)
    {
        var index = NavItems
            .Select((item, i) => (item, i))
            .FirstOrDefault(pair => pair.item.Key == key).i;

        SelectedNavIndex = Math.Clamp(index, 0, NavItems.Count - 1);
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        if (value < 0 || value >= NavItems.Count)
            return;

        var item = NavItems[value];
        CurrentTitle = PageTitles[value];
        CurrentSubtitle = PageSubtitles[value];
        CurrentPage = item.Key switch
        {
            "install" => Install,
            "launch" => Launch,
            "settings" => SettingsPage,
            "about" => About,
            _ => Home
        };

        if (item.Key == "launch")
            Launch.RefreshList();
    }
}
