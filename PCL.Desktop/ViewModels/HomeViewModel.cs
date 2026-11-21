using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
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
    }

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
}
