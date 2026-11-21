using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;
using PCL.Kernel.Java;

namespace PCL.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly LauncherServices _services;

    public SettingsViewModel(LauncherServices services)
    {
        _services = services;
        gameDirectory = services.Paths.GameDirectory;
        javaPath = services.Settings.JavaPath ?? "";
        memoryMb = services.Settings.MaxMemoryMb;
        username = services.Settings.OfflineUsername;
    }

    [ObservableProperty]
    private string gameDirectory;

    [ObservableProperty]
    private string javaPath;

    [ObservableProperty]
    private double memoryMb;

    [ObservableProperty]
    private string username;

    [ObservableProperty]
    private string status = "设置会自动保存";

    public string PlatformSummary =>
        $"{_services.Platform.OsName} · {_services.Platform.Architecture} · {_services.Platform.DataDirectory}";

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(GameDirectory))
        {
            Status = "游戏目录不能为空";
            return;
        }

        try
        {
            _services.Paths.GameDirectory = GameDirectory;
            _services.Paths.EnsureDirectories();
            _services.Settings.JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
            _services.Settings.MaxMemoryMb = Math.Clamp((int)MemoryMb, 512, 65536);
            _services.Settings.OfflineUsername =
                string.IsNullOrWhiteSpace(Username) ? "Player" : Username;
            _services.SaveSettings();

            GameDirectory = _services.Paths.GameDirectory;
            Status = "设置已保存";
        }
        catch (Exception ex)
        {
            Status = "保存失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = "正在检测 Java…";

        try
        {
            var java = await JavaLocator.FindAsync();
            if (java is null)
            {
                Status = "未找到 Java，请手动填写路径";
                return;
            }

            JavaPath = java.Path;
            Status = $"已找到 {java.DisplayName}";
        }
        catch (Exception ex)
        {
            Status = "Java 检测失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_services.Paths.DataDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Status = "无法打开数据目录：" + ex.Message;
        }
    }
}
