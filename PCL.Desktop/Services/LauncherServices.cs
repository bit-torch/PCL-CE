using PCL.Desktop.Localization;
using PCL.Kernel;
using PCL.Kernel.Abstractions;
using PCL.Kernel.Accounts;
using PCL.Kernel.App;
using PCL.Kernel.Homepage;
using PCL.Kernel.Install;
using PCL.Kernel.Java;
using PCL.Kernel.Launch;
using PCL.Kernel.Loaders;
using PCL.Kernel.Mojang;
using PCL.Kernel.Net;
using PCL.Kernel.Resources;
using PCL.Kernel.Updates;
using PCL.Kernel.Versions;

namespace PCL.Desktop.Services;

/// <summary>
/// 桌面层组合根。内核不依赖任何 UI 框架，平台能力通过接口注入。
/// </summary>
public sealed class LauncherServices : IDisposable
{
    public LauncherServices()
    {
        Paths = LauncherPaths.CreateDefault();
        Paths.EnsureDirectories();

        Settings = SettingsStore.Load(Paths.SettingsPath);
        Settings.Normalize();

        Localization = new LocalizationService(
            string.IsNullOrWhiteSpace(Settings.Language) ? null : Settings.Language);
        Loc.Initialize(Localization);

        if (!string.IsNullOrWhiteSpace(Settings.GameDirectory))
        {
            Paths.GameDirectory = Settings.GameDirectory;
            Paths.EnsureDirectories();
        }

        Downloads = new DownloadClient(Settings.Download);
        Manifest = new VersionManifestService(Paths, Downloads);
        Installer = new VanillaInstaller(Paths, Downloads, Manifest);
        JavaRuntimes = new JavaRuntimeService(Paths, Downloads);
        JavaInstaller = new JavaRuntimeInstaller(Paths, Downloads, JavaRuntimes);
        LoaderMeta = new LoaderMetaService(Paths, Downloads);
        LoaderInstaller = new LoaderInstaller(Paths, Downloads, LoaderMeta);
        ForgeLoaders = new ForgeLoaderInstaller(Paths, Downloads);
        LegacyLoaders = new LegacyLoaderService(Paths, Downloads);
        MicrosoftAuth = new MicrosoftAuthenticator();
        YggdrasilAuth = new YggdrasilAuthenticator();
        AuthlibInjector = new AuthlibInjectorService(Paths, Downloads);
        Modrinth = new ModrinthClient();
        ResourceInstaller = new ResourceInstaller(Paths, Downloads, Modrinth);
        Favorites = new FavoritesStore(Path.Combine(Paths.DataDirectory, "favorites.json"));
        News = new NewsService();
        Issues = new GitHubIssueClient();
        UpdateChecker = new UpdateChecker();
        UpdateInstaller = new UpdateInstaller(
            Downloads, Path.Combine(Paths.CacheDirectory, "updates"));
        VersionManager = new VersionManager(Paths);
        InstanceSettings = InstanceSettingsStore.Load(Paths);
        Launcher = new GameLauncher(Paths);
        UrlOpener = new SystemUrlOpener();
        Platform = new DesktopPlatformInfo(Paths);
    }

    public LauncherPaths Paths { get; }

    public LocalizationService Localization { get; }

    public DownloadClient Downloads { get; }

    public VersionManifestService Manifest { get; }

    public VanillaInstaller Installer { get; }

    public JavaRuntimeService JavaRuntimes { get; }

    public JavaRuntimeInstaller JavaInstaller { get; }

    public LoaderMetaService LoaderMeta { get; }

    public LoaderInstaller LoaderInstaller { get; }

    public ForgeLoaderInstaller ForgeLoaders { get; }

    public LegacyLoaderService LegacyLoaders { get; }

    public MicrosoftAuthenticator MicrosoftAuth { get; }

    public YggdrasilAuthenticator YggdrasilAuth { get; }

    public AuthlibInjectorService AuthlibInjector { get; }

    public ModrinthClient Modrinth { get; }

    public ResourceInstaller ResourceInstaller { get; }

    public FavoritesStore Favorites { get; }

    public NewsService News { get; }

    public GitHubIssueClient Issues { get; }

    public UpdateChecker UpdateChecker { get; }

    public UpdateInstaller UpdateInstaller { get; }

    public VersionManager VersionManager { get; }

    public InstanceSettingsStore InstanceSettings { get; }

    public GameLauncher Launcher { get; }

    public LauncherSettings Settings { get; }

    public IUrlOpener UrlOpener { get; }

    public IPlatformInfo Platform { get; }

    public void SaveSettings()
    {
        Settings.GameDirectory = Paths.GameDirectory;
        SettingsStore.Save(Paths.SettingsPath, Settings);
    }

    public void Dispose()
    {
        Downloads.Dispose();
        Modrinth.Dispose();
        News.Dispose();
        Issues.Dispose();
        UpdateChecker.Dispose();
    }
}
