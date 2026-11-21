using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
using PCL.Kernel.Install;
using PCL.Kernel.Java;
using PCL.Kernel.Loaders;
using PCL.Kernel.Mojang;
using PCL.Kernel.Versions;

namespace PCL.Desktop.ViewModels;

public partial class InstallViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;
    private readonly Action _onInstalled;

    public InstallViewModel(LauncherServices services, Action onInstalled)
    {
        _services = services;
        _strings = services.Localization;
        _onInstalled = onInstalled;

        stage = _strings["install.stage.waiting"];
        status = _strings["install.status.reading"];
        loaderStage = _strings["install.stage.waiting"];
        loaderStatus = _strings["install.loader.scanning"];

        SelectedLoader = LoaderOptions[0];
        RefreshInstalled();
        _ = LoadVersionsAsync();
    }

    public ObservableCollection<VersionSummary> Versions { get; } = [];

    public ObservableCollection<InstalledVersion> InstalledVersions { get; } = [];

    public IReadOnlyList<LoaderOption> LoaderOptions { get; } =
    [
        new(LoaderKind.Fabric, "Fabric"),
        new(LoaderKind.Quilt, "Quilt"),
        new(LoaderKind.LegacyFabric, "Legacy Fabric"),
        new(LoaderKind.Forge, "Forge"),
        new(LoaderKind.NeoForge, "NeoForge"),
        new(LoaderKind.OptiFine, "OptiFine"),
        new(LoaderKind.LiteLoader, "LiteLoader")
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
    private string stage;

    [ObservableProperty]
    private string status;

    [ObservableProperty]
    private bool isLoaderBusy;

    [ObservableProperty]
    private double loaderProgress;

    [ObservableProperty]
    private string loaderStage;

    [ObservableProperty]
    private string loaderStatus;

    partial void OnSelectedBaseVersionChanged(InstalledVersion? value) =>
        LoadLoaderVersionsCommand.Execute(null);

    partial void OnSelectedLoaderChanged(LoaderOption? value) =>
        LoadLoaderVersionsCommand.Execute(null);

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        try
        {
            Status = _strings["install.status.reading"];
            var manifest = await _services.Manifest.GetManifestAsync(forceRefresh: true);

            Versions.Clear();
            foreach (var version in manifest.Versions.Where(v =>
                         v.Type is "release" or "snapshot"))
                Versions.Add(version);

            SelectedVersion = Versions.FirstOrDefault(v =>
                                  string.Equals(v.Id, manifest.Latest.Release,
                                      StringComparison.OrdinalIgnoreCase))
                              ?? Versions.FirstOrDefault();

            Status = string.Format(_strings["install.status.loaded"], Versions.Count);
        }
        catch (Exception ex)
        {
            Status = _strings["install.error.manifest"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy)
            return;

        if (SelectedVersion is null)
        {
            Status = _strings["install.status.selectVersion"];
            return;
        }

        IsBusy = true;
        Progress = 0;
        Stage = _strings["install.stage.prepare"];

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
            Status = string.Format(_strings["install.status.done"], SelectedVersion.Id);
            RefreshInstalled();
            _onInstalled();
        }
        catch (Exception ex)
        {
            Status = _strings["install.error.install"] + ex.Message;
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
            LoaderStatus = _strings["install.loader.noBase"];
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
            LoaderStatus = _strings["install.loader.reading"];
            var versions = SelectedLoader.Kind switch
            {
                LoaderKind.OptiFine => await _services.LegacyLoaders.GetOptiFineVersionsAsync(
                    SelectedBaseVersion.Id),
                LoaderKind.LiteLoader => await _services.LegacyLoaders.GetLiteLoaderVersionsAsync(
                    SelectedBaseVersion.Id),
                LoaderKind.Forge or LoaderKind.NeoForge =>
                    await _services.ForgeLoaders.GetVersionsAsync(
                        SelectedLoader.Kind, SelectedBaseVersion.Id),
                _ => await _services.LoaderMeta.GetLoaderVersionsAsync(
                    SelectedLoader.Kind, SelectedBaseVersion.Id)
            };

            LoaderVersions.Clear();
            foreach (var version in versions)
                LoaderVersions.Add(version);

            SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.Stable)
                                    ?? LoaderVersions.FirstOrDefault();

            LoaderStatus = LoaderVersions.Count == 0
                ? string.Format(_strings["install.loader.none"], SelectedBaseVersion.Id,
                    SelectedLoader.Label)
                : string.Format(_strings["install.loader.loaded"], LoaderVersions.Count,
                    SelectedLoader.Label);
        }
        catch (Exception ex)
        {
            LoaderStatus = _strings["install.loader.error"] + ex.Message;
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
            LoaderStatus = _strings["install.loader.select"];
            return;
        }

        IsLoaderBusy = true;
        LoaderProgress = 0;
        LoaderStage = _strings["install.loader.stage.prepare"];

        try
        {
            var reporter = new Progress<InstallProgress>(value =>
            {
                LoaderProgress = value.Fraction * 100;
                LoaderStage = value.Total > 0
                    ? $"{value.Stage} {value.Completed}/{value.Total}"
                    : value.Stage;
            });

            LoaderInstallResult result;

            if (IsForge(SelectedLoader.Kind))
            {
                var javaPath = await ResolveJavaAsync();
                if (javaPath is null)
                {
                    LoaderStatus = _strings["install.loader.javaRequired"];
                    return;
                }

                result = await _services.ForgeLoaders.InstallAsync(
                    SelectedLoader.Kind,
                    SelectedBaseVersion.Id,
                    SelectedLoaderVersion.Version,
                    javaPath,
                    reporter);
            }
            else if (SelectedLoader.Kind == LoaderKind.OptiFine)
            {
                var javaPath = await ResolveJavaAsync();
                if (javaPath is null)
                {
                    LoaderStatus = _strings["install.loader.javaRequired"];
                    return;
                }

                result = await _services.LegacyLoaders.InstallOptiFineAsync(
                    SelectedBaseVersion.Id,
                    SelectedLoaderVersion.Version,
                    javaPath,
                    reporter);
            }
            else if (SelectedLoader.Kind == LoaderKind.LiteLoader)
            {
                result = await _services.LegacyLoaders.InstallLiteLoaderAsync(
                    SelectedBaseVersion.Id,
                    SelectedLoaderVersion.Version,
                    reporter);
            }
            else
            {
                result = await _services.LoaderInstaller.InstallAsync(
                    SelectedLoader.Kind,
                    SelectedBaseVersion.Id,
                    SelectedLoaderVersion.Version,
                    reporter);
            }

            LoaderStatus = string.Format(_strings["install.loader.done"], result.Id,
                result.LibraryCount);
            RefreshInstalled();
            _onInstalled();
        }
        catch (Exception ex)
        {
            LoaderStatus = _strings["install.loader.failed"] + ex.Message;
        }
        finally
        {
            IsLoaderBusy = false;
        }
    }

    private static bool IsForge(LoaderKind kind) =>
        kind is LoaderKind.Forge or LoaderKind.NeoForge;

    private async Task<string?> ResolveJavaAsync()
    {
        if (!string.IsNullOrWhiteSpace(_services.Settings.JavaPath) &&
            File.Exists(_services.Settings.JavaPath))
            return _services.Settings.JavaPath;

        var java = await JavaLocator.FindAsync();
        if (java is null)
            return null;

        _services.Settings.JavaPath = java.Path;
        _services.SaveSettings();
        return java.Path;
    }
}

public sealed record LoaderOption(LoaderKind Kind, string Label)
{
    public override string ToString() => Label;
}
