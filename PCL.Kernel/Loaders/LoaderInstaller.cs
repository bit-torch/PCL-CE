using System.Text.Json;
using PCL.Kernel.Install;
using PCL.Kernel.Mojang;
using PCL.Kernel.Net;

namespace PCL.Kernel.Loaders;

/// <summary>
/// 安装 Fabric / Quilt 加载器：写入加载器版本 JSON，并下载其依赖库。
/// 客户端 JAR、资源与参数继承自动来自已安装的原版版本。
/// </summary>
public sealed class LoaderInstaller
{
    private const int MaxConcurrentDownloads = 6;

    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;
    private readonly LoaderMetaService _meta;

    public LoaderInstaller(
        LauncherPaths paths,
        DownloadClient downloads,
        LoaderMetaService meta)
    {
        _paths = paths;
        _downloads = downloads;
        _meta = meta;
    }

    public async Task<LoaderInstallResult> InstallAsync(
        LoaderKind kind,
        string baseVersionId,
        string loaderVersion,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.VersionJsonPath(baseVersionId)))
            throw new InvalidOperationException(
                $"请先安装原版 {baseVersionId}，再安装 {kind} 加载器。");

        progress?.Report(new InstallProgress("读取加载器元数据", 0, 1, 0));

        var profileJson = await _meta
            .GetProfileJsonAsync(kind, baseVersionId, loaderVersion, cancellationToken)
            .ConfigureAwait(false);

        var normalized = LoaderProfileNormalizer.Normalize(
            profileJson, LoaderMetaEndpoints.MavenBase(kind));

        var detail = JsonSerializer.Deserialize<VersionDetail>(normalized, JsonDefaults.Options)
                     ?? throw new InvalidDataException("无法解析加载器版本 JSON。");

        if (string.IsNullOrWhiteSpace(detail.Id))
            throw new InvalidDataException("加载器版本 JSON 缺少 id。");

        var directory = _paths.VersionDirectory(detail.Id);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
                Path.Combine(directory, detail.Id + ".json"),
                normalized,
                cancellationToken)
            .ConfigureAwait(false);

        var artifacts = detail.Libraries
            .Where(library => RuleEvaluator.IsAllowed(library.Rules))
            .Select(library => library.Downloads?.Artifact)
            .Where(artifact => artifact is not null &&
                               !string.IsNullOrWhiteSpace(artifact.Url) &&
                               !string.IsNullOrWhiteSpace(artifact.Path))
            .Select(artifact => artifact!)
            .GroupBy(artifact => artifact.Path)
            .Select(group => group.First())
            .ToList();

        var total = artifacts.Count;
        var completed = 0;

        using var gate = new SemaphoreSlim(MaxConcurrentDownloads);

        var tasks = artifacts.Select(async artifact =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var destination = Path.Combine(
                    _paths.LibrariesDirectory,
                    artifact.Path.Replace('/', Path.DirectorySeparatorChar));

                await _downloads.DownloadAsync(
                        artifact.Url,
                        destination,
                        string.IsNullOrWhiteSpace(artifact.Sha1) ? null : artifact.Sha1,
                        artifact.Size > 0 ? artifact.Size : null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var index = Interlocked.Increment(ref completed);
                var fraction = total == 0 ? 1 : (double)index / total;
                progress?.Report(new InstallProgress(
                    "下载加载器依赖", index, total, fraction));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return new LoaderInstallResult(detail.Id, baseVersionId, total);
    }
}
