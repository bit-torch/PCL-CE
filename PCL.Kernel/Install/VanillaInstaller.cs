using System.IO.Compression;
using System.Text.Json;
using PCL.Kernel.Mojang;
using PCL.Kernel.Net;

namespace PCL.Kernel.Install;

public sealed record InstallProgress(string Stage, int Completed, int Total, double Fraction);

/// <summary>
/// 原版安装器：版本 JSON、客户端、依赖库、natives、资源索引与资源对象。
/// 只使用 Mojang 官方源，不包含任何 PCL 专有逻辑。
/// </summary>
public sealed class VanillaInstaller
{
    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;
    private readonly VersionManifestService _manifest;

    public VanillaInstaller(
        LauncherPaths paths,
        DownloadClient downloads,
        VersionManifestService manifest)
    {
        _paths = paths;
        _downloads = downloads;
        _manifest = manifest;
    }

    public async Task<VersionDetail> InstallAsync(
        VersionSummary summary,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();

        progress?.Report(new InstallProgress("读取版本信息", 0, 1, 0));
        var detail = await _manifest.GetVersionDetailAsync(summary, cancellationToken)
            .ConfigureAwait(false);

        await InstallClientAsync(detail, progress, cancellationToken).ConfigureAwait(false);
        await InstallLibrariesAsync(detail, progress, cancellationToken).ConfigureAwait(false);
        await InstallAssetsAsync(detail, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new InstallProgress("安装完成", 1, 1, 1));
        return detail;
    }

    private async Task InstallClientAsync(
        VersionDetail detail,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (detail.Downloads?.Client is not { } client ||
            string.IsNullOrWhiteSpace(client.Url))
            return;

        progress?.Report(new InstallProgress("下载客户端", 0, 1, 0));
        await _downloads.DownloadAsync(
                client.Url,
                _paths.VersionJarPath(detail.Id),
                client.Sha1,
                client.Size > 0 ? client.Size : null,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new InstallProgress("下载客户端", 1, 1, 1));
    }

    private async Task InstallLibrariesAsync(
        VersionDetail detail,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var libraries = detail.Libraries
            .Where(library => RuleEvaluator.IsAllowed(library.Rules))
            .ToList();

        var total = libraries.Count;
        var completed = 0;

        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (library.Downloads?.Artifact is { } artifact &&
                !string.IsNullOrWhiteSpace(artifact.Url))
            {
                var relativePath = string.IsNullOrWhiteSpace(artifact.Path)
                    ? MavenPath.FromCoordinate(library.Name)
                    : artifact.Path;

                await _downloads.DownloadAsync(
                        artifact.Url,
                        Path.Combine(_paths.LibrariesDirectory,
                            relativePath.Replace('/', Path.DirectorySeparatorChar)),
                        artifact.Sha1,
                        artifact.Size > 0 ? artifact.Size : null,
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (library.Natives is { } natives &&
                natives.TryGetValue(PlatformInfo.OsName, out var classifier))
            {
                classifier = classifier.Replace("${arch}", PlatformInfo.ArchBits);

                if (library.Downloads?.Classifiers?.GetValueOrDefault(classifier) is { } nativeArtifact &&
                    !string.IsNullOrWhiteSpace(nativeArtifact.Url))
                {
                    var nativePath = Path.Combine(
                        _paths.LibrariesDirectory,
                        (string.IsNullOrWhiteSpace(nativeArtifact.Path)
                            ? MavenPath.FromCoordinate(library.Name + ":" + classifier)
                            : nativeArtifact.Path).Replace('/', Path.DirectorySeparatorChar));

                    await _downloads.DownloadAsync(
                            nativeArtifact.Url,
                            nativePath,
                            nativeArtifact.Sha1,
                            nativeArtifact.Size > 0 ? nativeArtifact.Size : null,
                            null,
                            cancellationToken)
                        .ConfigureAwait(false);

                    ExtractNatives(nativePath, _paths.NativesDirectory(detail.Id));
                }
            }

            completed++;
            progress?.Report(new InstallProgress(
                "下载依赖库",
                completed,
                total,
                total == 0 ? 1 : (double)completed / total));
        }
    }

    private async Task InstallAssetsAsync(
        VersionDetail detail,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (detail.AssetIndex is not { } assetIndex ||
            string.IsNullOrWhiteSpace(assetIndex.Url))
            return;

        var indexPath = _paths.AssetIndexPath(assetIndex.Id);
        await _downloads.DownloadAsync(
                assetIndex.Url,
                indexPath,
                assetIndex.Sha1,
                assetIndex.Size > 0 ? assetIndex.Size : null,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        var json = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
        var index = JsonSerializer.Deserialize<AssetIndexFile>(json, JsonDefaults.Options)
                    ?? throw new InvalidDataException("无法解析资源索引。");

        var total = index.Objects.Count;
        var completed = 0;

        await Parallel.ForEachAsync(
                index.Objects,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 8,
                    CancellationToken = cancellationToken
                },
                async (entry, token) =>
                {
                    var (name, asset) = entry;
                    if (string.IsNullOrWhiteSpace(asset.Hash) || asset.Hash.Length < 2)
                        return;

                    var prefix = asset.Hash[..2];
                    var objectPath = Path.Combine(
                        _paths.AssetsDirectory, "objects", prefix, asset.Hash);
                    var url = $"https://resources.download.minecraft.net/{prefix}/{asset.Hash}";

                    await _downloads.DownloadAsync(
                            url,
                            objectPath,
                            asset.Hash,
                            asset.Size > 0 ? asset.Size : null,
                            null,
                            token)
                        .ConfigureAwait(false);

                    if (assetIndex.Virtual)
                    {
                        var virtualPath = Path.Combine(
                            _paths.AssetsDirectory, "virtual", "legacy",
                            name.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(virtualPath)!);
                        File.Copy(objectPath, virtualPath, true);
                    }

                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new InstallProgress(
                        "下载资源文件",
                        done,
                        total,
                        total == 0 ? 1 : (double)done / total));
                })
            .ConfigureAwait(false);
    }

    private static void ExtractNatives(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.Combine(
                destinationDirectory,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }
}
