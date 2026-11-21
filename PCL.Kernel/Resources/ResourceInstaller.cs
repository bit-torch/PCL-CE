using PCL.Kernel.Install;
using PCL.Kernel.Net;

namespace PCL.Kernel.Resources;

/// <summary>
/// 将资源版本及其必选依赖下载到游戏目录。文件按资源类型写入对应的子目录。
/// </summary>
public sealed class ResourceInstaller
{
    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;
    private readonly ModrinthClient _modrinth;

    public ResourceInstaller(
        LauncherPaths paths,
        DownloadClient downloads,
        ModrinthClient modrinth)
    {
        _paths = paths;
        _downloads = downloads;
        _modrinth = modrinth;
    }

    public async Task<ResourceInstallResult> InstallAsync(
        ResourceVersion version,
        ResourceType type,
        string? gameVersion = null,
        string? loader = null,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new InstallProgress("解析依赖", 0, 1, 0));
        var dependencies = await _modrinth.ResolveRequiredDependenciesAsync(
                version, gameVersion, loader, cancellationToken)
            .ConfigureAwait(false);

        var targets = new List<ResourceVersion> { version };
        targets.AddRange(dependencies);

        var directory = TargetDirectory(type);
        Directory.CreateDirectory(directory);

        var installed = new List<string>();
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var file = target.PrimaryFile
                       ?? throw new InvalidDataException(
                           $"资源版本 {target.DisplayName} 没有可下载文件。");

            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name))
                name = $"{target.ProjectId}-{target.Id}.jar";

            var destination = Path.Combine(directory, name);
            progress?.Report(new InstallProgress(
                $"下载 {name}", index, targets.Count,
                (double)index / targets.Count));

            await _downloads.DownloadAsync(
                    file.Url,
                    destination,
                    sha1: file.Sha1,
                    size: file.Size > 0 ? file.Size : null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            installed.Add(name);
        }

        progress?.Report(new InstallProgress("安装完成", targets.Count, targets.Count, 1));
        return new ResourceInstallResult(installed, directory);
    }

    public string TargetDirectory(ResourceType type) => type switch
    {
        ResourceType.Mod => Path.Combine(_paths.GameDirectory, "mods"),
        ResourceType.ResourcePack => Path.Combine(_paths.GameDirectory, "resourcepacks"),
        ResourceType.Shader => Path.Combine(_paths.GameDirectory, "shaderpacks"),
        ResourceType.DataPack => Path.Combine(_paths.GameDirectory, "datapacks"),
        _ => Path.Combine(_paths.GameDirectory, "mods")
    };
}
