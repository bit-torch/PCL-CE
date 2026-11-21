using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;
using PCL.Kernel.Java;
using PCL.Kernel.Versions;

namespace PCL.Desktop.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly Action<string> _navigate;

    public HomeViewModel(LauncherServices services, Action<string> navigate)
    {
        _services = services;
        _navigate = navigate;
        gameDirectory = services.Paths.GameDirectory;
        _ = RefreshAsync();
    }

    [ObservableProperty]
    private int installedCount;

    [ObservableProperty]
    private string javaStatus = "正在检测…";

    [ObservableProperty]
    private string gameDirectory;

    [ObservableProperty]
    private string latestRelease = "—";

    [ObservableProperty]
    private string status = "内核已就绪";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        InstalledCount = InstalledVersionScanner.Scan(_services.Paths).Count;
        GameDirectory = _services.Paths.GameDirectory;

        try
        {
            var java = await JavaLocator.FindAsync();
            JavaStatus = java?.DisplayName ?? "未找到 Java";
        }
        catch (Exception ex)
        {
            JavaStatus = "检测失败";
            Status = ex.Message;
        }

        try
        {
            var manifest = await _services.Manifest.GetManifestAsync();
            LatestRelease = manifest.Latest.Release;
        }
        catch (Exception ex)
        {
            LatestRelease = "读取失败";
            Status = "版本清单读取失败：" + ex.Message;
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
            Status = "无法打开游戏目录：" + ex.Message;
        }
    }
}
