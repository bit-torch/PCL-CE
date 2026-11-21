using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;
using PCL.Kernel.Install;
using PCL.Kernel.Mojang;

namespace PCL.Desktop.ViewModels;

public partial class InstallViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly Action _onInstalled;

    public InstallViewModel(LauncherServices services, Action onInstalled)
    {
        _services = services;
        _onInstalled = onInstalled;
        _ = LoadVersionsAsync();
    }

    public ObservableCollection<VersionSummary> Versions { get; } = [];

    [ObservableProperty]
    private VersionSummary? selectedVersion;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string stage = "等待开始";

    [ObservableProperty]
    private string status = "正在读取版本清单…";

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        try
        {
            Status = "正在读取版本清单…";
            var manifest = await _services.Manifest.GetManifestAsync(forceRefresh: true);

            Versions.Clear();
            foreach (var version in manifest.Versions.Where(v =>
                         v.Type is "release" or "snapshot"))
                Versions.Add(version);

            SelectedVersion = Versions.FirstOrDefault(v =>
                                  string.Equals(v.Id, manifest.Latest.Release,
                                      StringComparison.OrdinalIgnoreCase))
                              ?? Versions.FirstOrDefault();

            Status = $"已获取 {Versions.Count} 个版本";
        }
        catch (Exception ex)
        {
            Status = "读取版本清单失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy)
            return;

        if (SelectedVersion is null)
        {
            Status = "请先选择一个版本";
            return;
        }

        IsBusy = true;
        Progress = 0;
        Stage = "准备安装";

        try
        {
            var reporter = new Progress<InstallProgress>(value =>
            {
                Progress = value.Fraction * 100;
                Stage = value.Total > 0
                    ? $"{value.Stage} {value.Completed}/{value.Total}"
                    : value.Stage;
            });

            await _services.Installer.InstallAsync(SelectedVersion, reporter);
            Status = $"已完成安装：{SelectedVersion.Id}";
            _onInstalled();
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
