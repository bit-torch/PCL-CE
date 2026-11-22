using System.Text.Json;
using PCL.Kernel.Net;

namespace PCL.Kernel.Loaders;

/// <summary>
/// 读取 Fabric / Quilt Meta 的加载器版本与版本 JSON。
/// </summary>
public sealed class LoaderMetaService
{
    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;

    public LoaderMetaService(LauncherPaths paths, DownloadClient downloads)
    {
        _paths = paths;
        _downloads = downloads;
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetLoaderVersionsAsync(
        LoaderKind kind,
        string gameVersion,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var url = LoaderMetaEndpoints.VersionsUrl(kind, gameVersion);
        var cachePath = Path.Combine(
            _paths.CacheDirectory,
            $"loader-versions-{kind.ToString().ToLowerInvariant()}-{Sanitize(gameVersion)}.json");

        await EnsureJsonAsync(url, cachePath, TimeSpan.FromHours(6), forceRefresh, cancellationToken)
            .ConfigureAwait(false);

        var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
        return ParseLoaderVersions(json);
    }

    public async Task<string> GetProfileJsonAsync(
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken = default)
    {
        var url = LoaderMetaEndpoints.ProfileUrl(kind, gameVersion, loaderVersion);
        var cachePath = Path.Combine(
            _paths.CacheDirectory,
            $"loader-profile-{kind.ToString().ToLowerInvariant()}-" +
            $"{Sanitize(gameVersion)}-{Sanitize(loaderVersion)}.json");

        await EnsureJsonAsync(url, cachePath, TimeSpan.FromDays(30), false, cancellationToken)
            .ConfigureAwait(false);

        return await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<LoaderVersion> ParseLoaderVersions(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<LoaderVersion>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("loader", out var loader) ||
                !loader.TryGetProperty("version", out var versionElement))
                continue;

            var version = versionElement.GetString();
            if (string.IsNullOrWhiteSpace(version))
                continue;

            var stable = loader.TryGetProperty("stable", out var stableElement) &&
                         stableElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? stableElement.GetBoolean()
                : !version.Contains('-');

            result.Add(new LoaderVersion(version, stable));
        }

        return result;
    }

    private async Task EnsureJsonAsync(
        string url,
        string cachePath,
        TimeSpan maxAge,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var fresh = !forceRefresh &&
                    File.Exists(cachePath) &&
                    File.GetLastWriteTimeUtc(cachePath) > DateTime.UtcNow - maxAge;

        if (!fresh)
            await _downloads.DownloadAsync(url, cachePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
}
