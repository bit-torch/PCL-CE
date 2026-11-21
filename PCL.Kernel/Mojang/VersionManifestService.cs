using System.Text.Json;
using PCL.Kernel.Net;

namespace PCL.Kernel.Mojang;

/// <summary>
/// 获取 Mojang 版本清单与单个版本 JSON。版本 JSON 直接缓存到游戏目录的 versions 下。
/// </summary>
public sealed class VersionManifestService
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;
    private VersionManifest? _cachedManifest;

    public VersionManifestService(LauncherPaths paths, DownloadClient downloads)
    {
        _paths = paths;
        _downloads = downloads;
    }

    public async Task<VersionManifest> GetManifestAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cachedManifest is not null)
            return _cachedManifest;

        var cachePath = Path.Combine(_paths.CacheDirectory, "version_manifest_v2.json");
        var cacheIsFresh = File.Exists(cachePath) &&
                           File.GetLastWriteTimeUtc(cachePath) > DateTime.UtcNow.AddHours(-6);

        if (forceRefresh || !cacheIsFresh)
            await _downloads.DownloadAsync(ManifestUrl, cachePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
        _cachedManifest = JsonSerializer.Deserialize<VersionManifest>(json, JsonDefaults.Options)
                          ?? throw new InvalidDataException("无法解析 Mojang 版本清单。");

        return _cachedManifest;
    }

    public async Task<VersionDetail> GetVersionDetailAsync(
        VersionSummary summary,
        CancellationToken cancellationToken = default)
    {
        var path = _paths.VersionJsonPath(summary.Id);

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await _downloads
                .DownloadAsync(summary.Url, path, summary.Sha1, summary.Size,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadDetailAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VersionDetail?> FindVersionDetailAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var summary = manifest.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));

        return summary is null
            ? null
            : await GetVersionDetailAsync(summary, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<VersionDetail> ReadDetailAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<VersionDetail>(json, JsonDefaults.Options)
               ?? throw new InvalidDataException($"无法解析版本文件：{path}");
    }
}
