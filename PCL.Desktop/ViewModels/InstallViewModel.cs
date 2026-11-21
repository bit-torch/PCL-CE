using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;
using PCL.Kernel.Install;
using PCL.Kernel.Loaders;
using PCL.Kernel.Mojang;
using PCL.Kernel.Versions;

namespace PCL.Desktop.ViewModels;

public partial class InstallViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly Action _onInstalled;

    public InstallViewModel(LauncherServices services, Action onInstalled)
    {
        _services = services;
        _onInstalled = onInstalled;

        SelectedLoader = LoaderOptions[0];
        RefreshInstalled();
        _ = LoadVersionsAsync();
    }

    public ObservableCollection<VersionSummary> Versions { get; } = [];

    public ObservableCollection<InstalledVersion> InstalledVersions { get; } = [];

    public IReadOnlyList<LoaderOption> LoaderOptions { get; } =
    [
        new(LoaderKind.Fabric, "Fabric"),
        new(LoaderKind.Quilt, "Quilt")
    ];

    public ObservableCollection<LoaderVersion> LoaderVersions { get; } = [];

    [ObservableProperty]
    private VersionSummary? selectedVersion;

    [ObservableProperty]
    private InstalledVersion? selectedBaseVersion;

    [ObservableProperty]
    private LoaderOption? selectedLoader;

    [ObservableProperty]
    private LoaderVersion? selectedLoaderVersion;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string stage = "等待开始";

    [ObservableProperty]
    private string status = "正在读取版本清单…";

    [ObservableProperty]
    private bool isLoaderBusy;

    [ObservableProperty]
    private double loaderProgress;

    [ObservableProperty]
    private string loaderStage = "等待开始";

    [ObservableProperty]
    private string loaderStatus = "正在扫描已安装版本…";

    partial void OnSelectedBaseVersionChanged(InstalledVersion? value) =>
        LoadLoaderVersionsCommand.Execute(null);

    partial void OnSelectedLoaderChanged(LoaderOption? value) =>
        LoadLoaderVersionsCommand.Execute(null);

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
            RefreshInstalled();
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

    [RelayCommand]
    private void RefreshInstalled()
    {
        var previous = SelectedBaseVersion?.Id;

        InstalledVersions.Clear();
        foreach (var version in InstalledVersionScanner.Scan(_services.Paths)
                     .Where(v => string.IsNullOrWhiteSpace(v.InheritsFrom)))
            InstalledVersions.Add(version);

        SelectedBaseVersion = InstalledVersions.FirstOrDefault(v =>
                                  string.Equals(v.Id, previous, StringComparison.OrdinalIgnoreCase))
                              ?? InstalledVersions.FirstOrDefault();

        if (InstalledVersions.Count == 0)
            LoaderStatus = "请先安装一个原版版本";
    }

    [RelayCommand]
    private async Task LoadLoaderVersionsAsync()
    {
        if (SelectedBaseVersion is null || SelectedLoader is null)
        {
            LoaderVersions.Clear();
            return;
        }

        try
        {
            LoaderStatus = "正在读取加载器版本…";
            var versions = await _services.LoaderMeta.GetLoaderVersionsAsync(
                SelectedLoader.Kind, SelectedBaseVersion.Id);

            LoaderVersions.Clear();
            foreach (var version in versions)
                LoaderVersions.Add(version);

            SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.Stable)
                                    ?? LoaderVersions.FirstOrDefault();

            LoaderStatus = LoaderVersions.Count == 0
                ? $"{SelectedBaseVersion.Id} 没有可用的 {SelectedLoader.Label} 版本"
                : $"已获取 {LoaderVersions.Count} 个 {SelectedLoader.Label} 版本";
        }
        catch (Exception ex)
        {
            LoaderStatus = "读取加载器版本失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallLoaderAsync()
    {
        if (IsLoaderBusy)
            return;

        if (SelectedBaseVersion is null ||
            SelectedLoader is null ||
            SelectedLoaderVersion is null)
        {
            LoaderStatus = "请选择原版版本、加载器与加载器版本";
            return;
        }

        IsLoaderBusy = true;
        LoaderProgress = 0;
        LoaderStage = "准备安装加载器";

        try
        {
            var reporter = new Progress<InstallProgress>(value =>
            {
                LoaderProgress = value.Fraction * 100;
                LoaderStage = value.Total > 0
                    ? $"{value.Stage} {value.Completed}/{value.Total}"
                    : value.Stage;
            });

            var result = await _services.LoaderInstaller.InstallAsync(
                SelectedLoader.Kind,
                SelectedBaseVersion.Id,
                SelectedLoaderVersion.Version,
                reporter);

            LoaderStatus = $"已安装 {result.Id}（{result.LibraryCount} 个依赖库）";
            RefreshInstalled();
            _onInstalled();
        }
        catch (Exception ex)
        {
            LoaderStatus = "加载器安装失败：" + ex.Message;
        }
        finally
        {
            IsLoaderBusy = false;
        }
    }
}

public sealed record LoaderOption(LoaderKind Kind, string Label)
{
    public override string ToString() => Label;
}
