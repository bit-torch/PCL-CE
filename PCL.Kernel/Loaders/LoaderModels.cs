namespace PCL.Kernel.Loaders;

public enum LoaderKind
{
    Fabric,
    Quilt,
    Forge,
    NeoForge
}

public sealed record LoaderVersion(string Version, bool Stable)
{
    public string DisplayName => Stable ? $"{Version} · 稳定" : $"{Version} · 测试";
}

public sealed record LoaderInstallResult(string Id, string BaseVersionId, int LibraryCount);

public static class LoaderMetaEndpoints
{
    public const string ForgeMavenBase = "https://maven.minecraftforge.net/";
    public const string NeoForgeMavenBase = "https://maven.neoforged.net/releases/";

    public static string VersionsUrl(LoaderKind kind, string gameVersion) =>
        $"{BaseUrl(kind)}/{Uri.EscapeDataString(gameVersion)}";

    public static string ProfileUrl(LoaderKind kind, string gameVersion, string loaderVersion) =>
        $"{VersionsUrl(kind, gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";

    public static string MavenBase(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => "https://maven.fabricmc.net/",
        LoaderKind.Quilt => "https://maven.quiltmc.org/repository/release/",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string ForgeMavenMetadataUrl => ForgeMavenBase + "net/minecraftforge/forge/maven-metadata.xml";

    public static string NeoForgeMavenMetadataUrl =>
        NeoForgeMavenBase + "net/neoforged/neoforge/maven-metadata.xml";

    public static string ForgeInstallerUrl(string gameVersion, string loaderVersion) =>
        $"{ForgeMavenBase}net/minecraftforge/forge/" +
        $"{Uri.EscapeDataString(gameVersion)}-{Uri.EscapeDataString(loaderVersion)}/" +
        $"forge-{Uri.EscapeDataString(gameVersion)}-{Uri.EscapeDataString(loaderVersion)}-installer.jar";

    public static string NeoForgeInstallerUrl(string loaderVersion) =>
        $"{NeoForgeMavenBase}net/neoforged/neoforge/" +
        $"{Uri.EscapeDataString(loaderVersion)}/" +
        $"neoforge-{Uri.EscapeDataString(loaderVersion)}-installer.jar";

    public static string ForgeVersionId(string gameVersion, string loaderVersion) =>
        $"{gameVersion}-forge-{loaderVersion}";

    public static string NeoForgeVersionId(string loaderVersion) => $"neoforge-{loaderVersion}";

    private static string BaseUrl(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => "https://meta.fabricmc.net/v2/versions/loader",
        LoaderKind.Quilt => "https://meta.quiltmc.org/v3/versions/loader",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
