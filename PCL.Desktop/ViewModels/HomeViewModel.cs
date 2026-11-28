using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
using PCL.Kernel.Homepage;
using PCL.Kernel.Java;
using PCL.Kernel.Versions;

namespace PCL.Desktop.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;
    private readonly Action<string> _navigate;

    public HomeViewModel(LauncherServices services, Action<string> navigate)
    {
        _services = services;
        _strings = services.Localization;
        _navigate = navigate;
        gameDirectory = services.Paths.GameDirectory;
        javaStatus = _strings["home.java.detecting"];
        status = _strings["home.status.ready"];
        _ = RefreshAsync();
        _ = LoadNewsAsync();
    }

    /// <summary>Minecraft 官网信息流，与 WPF 首页的新闻卡片保持一致的数据来源。</summary>
    public ObservableCollection<NewsItem> NewsItems { get; } = [];

    public bool HasNews => NewsItems.Count > 0;

    public bool HasNewsError => !string.IsNullOrEmpty(NewsError);

    public string GameDirectoryLabel => string.Format(_strings["home.next.path"], GameDirectory);

    [ObservableProperty]
    private int installedCount;

    [ObservableProperty]
    private string javaStatus;

    [ObservableProperty]
    private string gameDirectory;

    [ObservableProperty]
    private string latestRelease = "—";

    [ObservableProperty]
    private string status;

    [ObservableProperty]
    private bool isNewsLoading;

    [ObservableProperty]
    private string? newsError;

    partial void OnNewsErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasNewsError));

    partial void OnGameDirectoryChanged(string value) =>
        OnPropertyChanged(nameof(GameDirectoryLabel));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        InstalledCount = InstalledVersionScanner.Scan(_services.Paths).Count;
        GameDirectory = _services.Paths.GameDirectory;

        try
        {
            var java = await JavaLocator.FindAsync();
            JavaStatus = java?.DisplayName ?? _strings["home.java.missing"];
        }
        catch (Exception ex)
        {
            JavaStatus = _strings["home.java.failed"];
            Status = ex.Message;
        }

        try
        {
            var manifest = await _services.Manifest.GetManifestAsync();
            LatestRelease = manifest.Latest.Release;
        }
        catch (Exception ex)
        {
            LatestRelease = _strings["home.latest.failed"];
            Status = _strings["home.error.manifest"] + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenInstall() => _navigate("install");

    [RelayCommand]
    private void OpenLaunch() => _navigate("launch");

    [RelayCommand]
    private void OpenSettings() => _navigate("settings");

    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GameDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = _strings["home.error.openDir"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadNewsAsync()
    {
        if (IsNewsLoading)
            return;

        IsNewsLoading = true;
        NewsError = null;

        try
        {
            var items = await _services.News.GetLatestAsync();
            NewsItems.Clear();
            foreach (var item in items)
                NewsItems.Add(item);

            OnPropertyChanged(nameof(HasNews));
        }
        catch (Exception ex)
        {
            NewsError = _strings["home.news.error"] + ex.Message;
        }
        finally
        {
            IsNewsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenNewsAsync(NewsItem? item)
    {
        if (item is null || !NewsService.IsSafeNewsLink(item.Url))
            return;

        try
        {
            await _services.UrlOpener.OpenAsync(new Uri(item.Url));
        }
        catch (Exception ex)
        {
            NewsError = _strings["home.news.error"] + ex.Message;
        }
    }
}
