using PCL.Kernel;
using PCL.Kernel.Abstractions;
using PCL.Kernel.App;
using PCL.Kernel.Install;
using PCL.Kernel.Java;
using PCL.Kernel.Launch;
using PCL.Kernel.Mojang;
using PCL.Kernel.Net;

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

        if (!string.IsNullOrWhiteSpace(Settings.GameDirectory))
        {
            Paths.GameDirectory = Settings.GameDirectory;
            Paths.EnsureDirectories();
        }

        Downloads = new DownloadClient();
        Manifest = new VersionManifestService(Paths, Downloads);
        Installer = new VanillaInstaller(Paths, Downloads, Manifest);
        JavaRuntimes = new JavaRuntimeService(Paths, Downloads);
        JavaInstaller = new JavaRuntimeInstaller(Paths, Downloads, JavaRuntimes);
        Launcher = new GameLauncher(Paths);
        UrlOpener = new SystemUrlOpener();
        Platform = new DesktopPlatformInfo(Paths);
    }

    public LauncherPaths Paths { get; }

    public DownloadClient Downloads { get; }

    public VersionManifestService Manifest { get; }

    public VanillaInstaller Installer { get; }

    public JavaRuntimeService JavaRuntimes { get; }

    public JavaRuntimeInstaller JavaInstaller { get; }

    public GameLauncher Launcher { get; }

    public LauncherSettings Settings { get; }

    public IUrlOpener UrlOpener { get; }

    public IPlatformInfo Platform { get; }

    public void SaveSettings()
    {
        Settings.GameDirectory = Paths.GameDirectory;
        SettingsStore.Save(Paths.SettingsPath, Settings);
    }

    public void Dispose() => Downloads.Dispose();
}
