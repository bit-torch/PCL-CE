using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;

namespace PCL.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly string[] PageTitleKeys =
    [
        "page.home.title",
        "page.install.title",
        "page.launch.title",
        "page.settings.title",
        "page.about.title"
    ];

    private static readonly string[] PageSubtitleKeys =
    [
        "page.home.subtitle",
        "page.install.subtitle",
        "page.launch.subtitle",
        "page.settings.subtitle",
        "page.about.subtitle"
    ];

    private readonly LocalizationService _strings;

    public MainWindowViewModel(LauncherServices services, Func<Task<bool>> licenseGate)
    {
        _strings = services.Localization;
        NavItems = NavItem.CreateDefault(_strings);

        Launch = new LaunchViewModel(services, licenseGate);
        Install = new InstallViewModel(services, Launch.RefreshList);
        SettingsPage = new SettingsViewModel(services);
        About = new AboutViewModel(services);
        Home = new HomeViewModel(services, NavigateTo);

        currentPage = Home;
        currentTitle = _strings[PageTitleKeys[0]];
        currentSubtitle = _strings[PageSubtitleKeys[0]];
        statusText = _strings["shell.status"];
    }

    public IReadOnlyList<NavItem> NavItems { get; }

    public HomeViewModel Home { get; }

    public InstallViewModel Install { get; }

    public LaunchViewModel Launch { get; }

    public SettingsViewModel SettingsPage { get; }

    public AboutViewModel About { get; }

    public string WindowTitle => _strings["app.title"];

    public string ShellBadge => _strings["shell.badge"];

    public string ShellVersion => _strings["shell.version"];

    [ObservableProperty]
    private int selectedNavIndex;

    [ObservableProperty]
    private ObservableObject currentPage;

    [ObservableProperty]
    private string currentTitle;

    [ObservableProperty]
    private string currentSubtitle;

    [ObservableProperty]
    private string statusText;

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
        CurrentTitle = _strings[PageTitleKeys[value]];
        CurrentSubtitle = _strings[PageSubtitleKeys[value]];
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
