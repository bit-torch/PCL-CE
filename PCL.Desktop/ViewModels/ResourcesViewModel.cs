using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
using PCL.Kernel.Install;
using PCL.Kernel.Resources;
using PCL.Kernel.Versions;

namespace PCL.Desktop.ViewModels;

/// <summary>资源搜索与安装。使用 Modrinth 公开接口，不含上游私有密钥。</summary>
public partial class ResourcesViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;

    public ResourcesViewModel(LauncherServices services)
    {
        _services = services;
        _strings = services.Localization;

        TypeOptions =
        [
            new(ResourceType.Mod, _strings["resources.type.mod"]),
            new(ResourceType.ResourcePack, _strings["resources.type.resourcepack"]),
            new(ResourceType.Shader, _strings["resources.type.shader"]),
            new(ResourceType.DataPack, _strings["resources.type.datapack"])
        ];

        LoaderOptions =
        [
            new(null, _strings["resources.loader.auto"]),
            new("fabric", "Fabric"),
            new("quilt", "Quilt"),
            new("forge", "Forge"),
            new("neoforge", "NeoForge")
        ];

        selectedType = TypeOptions[0];
        selectedLoader = LoaderOptions[0];
        status = _strings["resources.status.idle"];

        foreach (var target in InstanceTargets.Build(_services, _strings))
            Targets.Add(target);
        selectedTarget = Targets[0];

        RefreshFavorites();
        _ = LoadGameVersionsAsync();
    }

    public IReadOnlyList<ResourceTypeOption> TypeOptions { get; }

    public IReadOnlyList<ResourceLoaderOption> LoaderOptions { get; }

    public ObservableCollection<ResourceProject> Projects { get; } = [];

    public ObservableCollection<ResourceVersion> Versions { get; } = [];

    public ObservableCollection<FavoriteResource> Favorites { get; } = [];

    public ObservableCollection<string> GameVersions { get; } = [];

    /// <summary>全局游戏目录或启用隔离的实例目录。</summary>
    public ObservableCollection<InstanceTarget> Targets { get; } = [];

    [ObservableProperty]
    private InstanceTarget? selectedTarget;

    [ObservableProperty]
    private string searchQuery = "";

    [ObservableProperty]
    private ResourceTypeOption selectedType;

    [ObservableProperty]
    private ResourceLoaderOption selectedLoader;

    [ObservableProperty]
    private string? selectedGameVersion;

    [ObservableProperty]
    private ResourceProject? selectedProject;

    [ObservableProperty]
    private ResourceVersion? selectedVersion;

    [ObservableProperty]
    private FavoriteResource? selectedFavorite;

    public bool HasFavorites => Favorites.Count > 0;

    public bool HasSelectedProject => SelectedProject is not null;

    public bool HasSelectedFavorite => SelectedFavorite is not null;

    public bool IsSelectedFavorite =>
        SelectedProject is not null && _services.Favorites.Contains(SelectedProject.Id);

    public string FavoriteToggleLabel => IsSelectedFavorite
        ? _strings["resources.favorites.remove"]
        : _strings["resources.favorites.add"];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isInstalling;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string status;

    public bool ShowLoader => SelectedType?.Type == ResourceType.Mod;

    partial void OnSelectedTypeChanged(ResourceTypeOption value) =>
        OnPropertyChanged(nameof(ShowLoader));

    partial void OnSelectedProjectChanged(ResourceProject? value)
    {
        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(IsSelectedFavorite));
        OnPropertyChanged(nameof(FavoriteToggleLabel));
        _ = LoadVersionsAsync();
    }

    partial void OnSelectedFavoriteChanged(FavoriteResource? value) =>
        OnPropertyChanged(nameof(HasSelectedFavorite));

    [RelayCommand]
    private async Task LoadGameVersionsAsync()
    {
        try
        {
            var manifest = await _services.Manifest.GetManifestAsync();
            GameVersions.Clear();
            foreach (var version in manifest.Versions
                         .Where(item => item.Type == "release")
                         .Take(60))
                GameVersions.Add(version.Id);

            SelectedGameVersion = GameVersions.FirstOrDefault(id =>
                                      string.Equals(id, manifest.Latest.Release,
                                          StringComparison.OrdinalIgnoreCase))
                                  ?? GameVersions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Status = _strings["resources.error.manifest"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Status = _strings["resources.status.searching"];

        try
        {
            var result = await _services.Modrinth.SearchAsync(
                SearchQuery,
                SelectedType?.Type ?? ResourceType.Mod,
                SelectedGameVersion,
                SelectedLoader?.Value);

            Projects.Clear();
            foreach (var project in result.Projects)
                Projects.Add(project);

            Versions.Clear();
            SelectedProject = Projects.FirstOrDefault();

            Status = string.Format(_strings["resources.status.found"], Projects.Count);
        }
        catch (Exception ex)
        {
            Status = _strings["resources.error.search"] + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        Versions.Clear();

        if (SelectedProject is null)
            return;

        try
        {
            Status = _strings["resources.status.reading"];
            var versions = await _services.Modrinth.GetVersionsAsync(
                SelectedProject.Slug,
                SelectedGameVersion,
                SelectedLoader?.Value);

            foreach (var version in versions)
                Versions.Add(version);

            SelectedVersion = Versions.FirstOrDefault();
            Status = Versions.Count == 0
                ? _strings["resources.status.noVersions"]
                : string.Format(_strings["resources.status.versions"], Versions.Count);
        }
        catch (Exception ex)
        {
            Status = _strings["resources.error.versions"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsInstalling)
            return;

        if (SelectedVersion is null || SelectedType is null)
        {
            Status = _strings["resources.status.select"];
            return;
        }

        IsInstalling = true;
        Progress = 0;
        var type = SelectedType.Type;

        try
        {
            var reporter = new Progress<InstallProgress>(value =>
                Progress = Math.Clamp(value.Fraction * 100, 0, 100));

            var result = await _services.ResourceInstaller.InstallAsync(
                SelectedVersion,
                type,
                SelectedGameVersion,
                SelectedLoader?.Value,
                reporter,
                gameDirectory: SelectedTarget?.GameDirectory);

            Progress = 100;
            Status = string.Format(_strings["resources.status.done"],
                result.Files.Count, result.Directory);
        }
        catch (Exception ex)
        {
            Status = _strings["resources.error.install"] + ex.Message;
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedProject is not { } project)
        {
            Status = _strings["resources.favorites.select"];
            return;
        }

        var favorite = new FavoriteResource(
            project.Id,
            project.Slug,
            project.Title,
            project.Author,
            project.ProjectType,
            project.IconUrl);

        var added = _services.Favorites.Toggle(favorite);
        RefreshFavorites();
        OnPropertyChanged(nameof(IsSelectedFavorite));
        OnPropertyChanged(nameof(FavoriteToggleLabel));

        Status = added
            ? string.Format(_strings["resources.favorites.added"], project.Title)
            : string.Format(_strings["resources.favorites.removedItem"], project.Title);
    }

    [RelayCommand]
    private void RemoveFavorite()
    {
        if (SelectedFavorite is not { } favorite)
        {
            Status = _strings["resources.favorites.select"];
            return;
        }

        if (!_services.Favorites.Remove(favorite.Id))
            return;

        RefreshFavorites();
        OnPropertyChanged(nameof(IsSelectedFavorite));
        OnPropertyChanged(nameof(FavoriteToggleLabel));
        Status = string.Format(_strings["resources.favorites.removedItem"], favorite.Title);
    }

    [RelayCommand]
    private async Task OpenFavoriteAsync()
    {
        if (SelectedFavorite is not { } favorite)
        {
            Status = _strings["resources.favorites.select"];
            return;
        }

        SearchQuery = favorite.Title;
        await SearchAsync();
    }

    private void RefreshFavorites()
    {
        Favorites.Clear();
        foreach (var favorite in _services.Favorites.Items)
            Favorites.Add(favorite);

        OnPropertyChanged(nameof(HasFavorites));
        SelectedFavorite = Favorites.FirstOrDefault();
    }
}

public sealed record ResourceTypeOption(ResourceType Type, string Label)
{
    public override string ToString() => Label;
}

public sealed record ResourceLoaderOption(string? Value, string Label)
{
    public override string ToString() => Label;
}
