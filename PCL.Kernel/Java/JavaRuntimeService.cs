using System.Runtime.InteropServices;
using System.Text.Json;
using PCL.Kernel.Mojang;
using PCL.Kernel.Net;

namespace PCL.Kernel.Java;

/// <summary>
/// 读取 Mojang 官方 Java 运行时清单，并按当前平台选择组件。
/// </summary>
public sealed class JavaRuntimeService
{
    public const string ManifestUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/" +
        "2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public const string DefaultComponent = "java-runtime-gamma";

    private const string CacheFileName = "java_runtime_all.json";

    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;
    private JavaRuntimeCatalog? _catalog;

    public JavaRuntimeService(LauncherPaths paths, DownloadClient downloads)
    {
        _paths = paths;
        _downloads = downloads;
    }

    public static string GetPlatformKey()
    {
        var architecture = RuntimeInformation.OSArchitecture;

        if (OperatingSystem.IsWindows())
            return architecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "windows-arm64",
                System.Runtime.InteropServices.Architecture.X86 => "windows-x86",
                _ => "windows-x64"
            };

        if (OperatingSystem.IsMacOS())
            return architecture == System.Runtime.InteropServices.Architecture.Arm64
                ? "mac-os-arm64"
                : "mac-os";

        return architecture == System.Runtime.InteropServices.Architecture.X86
            ? "linux-i386"
            : "linux";
    }

    public async Task<JavaRuntimeCatalog> GetCatalogAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _catalog is not null)
            return _catalog;

        var cachePath = Path.Combine(_paths.CacheDirectory, CacheFileName);
        var cacheIsFresh = File.Exists(cachePath) &&
                           File.GetLastWriteTimeUtc(cachePath) > DateTime.UtcNow.AddHours(-12);

        if (forceRefresh || !cacheIsFresh)
            await _downloads.DownloadAsync(ManifestUrl, cachePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
        _catalog = JsonSerializer.Deserialize<JavaRuntimeCatalog>(json, JsonDefaults.Options)
                   ?? throw new InvalidDataException("无法解析 Mojang Java 运行时清单。");

        return _catalog;
    }

    /// <summary>从清单中挑选当前平台可用的组件；找不到返回 null。</summary>
    public static JavaRuntimePackage? SelectPackage(
        JavaRuntimeCatalog catalog,
        string component,
        string platformKey)
    {
        if (!catalog.TryGetValue(platformKey, out var components) ||
            !components.TryGetValue(component, out var entries))
            return null;

        var entry = entries.FirstOrDefault();
        if (entry?.Manifest is null || string.IsNullOrWhiteSpace(entry.Manifest.Url))
            return null;

        return new JavaRuntimePackage(
            component,
            platformKey,
            entry.Version?.Name ?? "",
            entry.Manifest.Url,
            entry.Manifest.Sha1,
            entry.Manifest.Size);
    }

    public async Task<JavaRuntimePackage> GetPackageAsync(
        string? component = null,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var platform = GetPlatformKey();

        foreach (var candidate in ComponentCandidates(catalog, platform, component))
        {
            var package = SelectPackage(catalog, candidate, platform);
            if (package is not null)
                return package;
        }

        throw new InvalidOperationException(
            $"当前平台（{platform}）没有可用的 Java 运行时组件：{component ?? DefaultComponent}");
    }

    private static IEnumerable<string> ComponentCandidates(
        JavaRuntimeCatalog catalog,
        string platform,
        string? component)
    {
        if (!catalog.TryGetValue(platform, out var components))
            yield break;

        if (!string.IsNullOrWhiteSpace(component) && components.ContainsKey(component))
            yield return component;

        if (!string.Equals(component, DefaultComponent, StringComparison.OrdinalIgnoreCase) &&
            components.ContainsKey(DefaultComponent))
            yield return DefaultComponent;

        foreach (var name in components.Keys
                     .Where(k => !string.Equals(k, component, StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(k, DefaultComponent, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(k => k.StartsWith("java-runtime", StringComparison.Ordinal))
                     .ThenBy(k => k, StringComparer.Ordinal))
            yield return name;
    }
}
