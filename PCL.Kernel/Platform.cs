using System.Runtime.InteropServices;

namespace PCL.Kernel;

public static class PlatformInfo
{
    public static string OsName =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "osx" :
        "linux";

    public static string DisplayName =>
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsMacOS() ? "macOS" :
        "Linux";

    public static string Architecture => RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.Arm => "arm",
        _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
    };

    public static string ArchBits => Environment.Is64BitProcess ? "64" : "32";

    public static string OsVersion => Environment.OSVersion.VersionString;
}

/// <summary>
/// 启动器的全部路径。数据目录与游戏目录分离，便于同时管理多个游戏目录。
/// </summary>
public sealed class LauncherPaths
{
    public LauncherPaths(string dataDirectory, string gameDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        GameDirectory = Path.GetFullPath(gameDirectory);
    }

    public string DataDirectory { get; }

    public string GameDirectory { get; set; }

    public string CacheDirectory => Path.Combine(DataDirectory, "cache");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public string SettingsPath => Path.Combine(DataDirectory, "launcher.settings.json");

    public string VersionsDirectory => Path.Combine(GameDirectory, "versions");

    public string LibrariesDirectory => Path.Combine(GameDirectory, "libraries");

    public string AssetsDirectory => Path.Combine(GameDirectory, "assets");

    public static LauncherPaths CreateDefault() =>
        new(GetDefaultDataDirectory(), GetDefaultGameDirectory());

    public string VersionDirectory(string versionId) =>
        Path.Combine(VersionsDirectory, versionId);

    public string VersionJsonPath(string versionId) =>
        Path.Combine(VersionDirectory(versionId), versionId + ".json");

    public string VersionJarPath(string versionId) =>
        Path.Combine(VersionDirectory(versionId), versionId + ".jar");

    public string NativesDirectory(string versionId) =>
        Path.Combine(VersionDirectory(versionId), "natives");

    public string AssetIndexPath(string assetIndexId) =>
        Path.Combine(AssetsDirectory, "indexes", assetIndexId + ".json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(GameDirectory);
        Directory.CreateDirectory(VersionsDirectory);
        Directory.CreateDirectory(LibrariesDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        Directory.CreateDirectory(Path.Combine(AssetsDirectory, "indexes"));
        Directory.CreateDirectory(Path.Combine(AssetsDirectory, "objects"));
    }

    private static string GetDefaultDataDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL CE");

        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "PCL CE");

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(configHome, "pcl-ce");
    }

    private static string GetDefaultGameDirectory()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "minecraft");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            return Path.Combine(appData, ".minecraft");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");
    }
}
