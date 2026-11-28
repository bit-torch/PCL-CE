using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Kernel.Mojang;

namespace PCL.Kernel.Versions;

public sealed record VersionVerifyReport(
    string VersionId,
    int CheckedFiles,
    int MissingFiles,
    IReadOnlyList<string> Missing)
{
    public bool IsComplete => MissingFiles == 0;
}

/// <summary>
/// 本地版本维护：文件校验、删除与版本隔离路径。
/// </summary>
public sealed class VersionManager
{
    private readonly LauncherPaths _paths;

    public VersionManager(LauncherPaths paths) => _paths = paths;

    public string ResolveGameDirectory(string versionId, bool isolate) =>
        isolate
            ? Path.Combine(_paths.VersionDirectory(versionId), "game")
            : _paths.GameDirectory;

    public async Task<VersionVerifyReport> VerifyAsync(
        InstalledVersion version,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedAssetIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedFiles = 0;

        var current = version;

        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            if (!visitedVersions.Add(current.Id))
                break;

            VersionDetail detail;
            try
            {
                detail = await VersionManifestService.ReadDetailAsync(
                    current.JsonPath, cancellationToken);
            }
            catch (Exception)
            {
                missing.Add($"版本 JSON：{current.Id}");
                break;
            }

            if (detail.Downloads?.Client is { } client &&
                !string.IsNullOrWhiteSpace(client.Path))
            {
                Check(
                    Path.Combine(current.Directory, client.Path),
                    client.Path);
            }

            foreach (var library in detail.Libraries
                         .Where(library => RuleEvaluator.IsAllowed(library.Rules)))
            {
                var artifact = library.Downloads?.Artifact;
                if (artifact is null || string.IsNullOrWhiteSpace(artifact.Path))
                    continue;

                Check(
                    Path.Combine(
                        _paths.LibrariesDirectory,
                        artifact.Path.Replace('/', Path.DirectorySeparatorChar)),
                    artifact.Path);
            }

            if (!string.IsNullOrWhiteSpace(detail.AssetIndex?.Id) &&
                checkedAssetIndexes.Add(detail.AssetIndex.Id))
            {
                checkedFiles++;
                var indexPath = _paths.AssetIndexPath(detail.AssetIndex.Id);

                if (!File.Exists(indexPath))
                {
                    missing.Add($"资源索引：{detail.AssetIndex.Id}");
                }
                else
                {
                    await CheckAssetsAsync(indexPath, cancellationToken);
                }
            }

            if (string.IsNullOrWhiteSpace(detail.InheritsFrom))
                break;

            var parentJson = _paths.VersionJsonPath(detail.InheritsFrom);
            if (!File.Exists(parentJson))
            {
                missing.Add($"版本 JSON：{detail.InheritsFrom}");
                break;
            }

            var parentJar = _paths.VersionJarPath(detail.InheritsFrom);
            current = new InstalledVersion(
                detail.InheritsFrom,
                _paths.VersionDirectory(detail.InheritsFrom),
                parentJson,
                File.Exists(parentJar) ? parentJar : null,
                "release",
                null);
        }

        return new VersionVerifyReport(version.Id, checkedFiles, missing.Count, missing);

        void Check(string path, string display)
        {
            if (!checkedPaths.Add(display))
                return;

            checkedFiles++;
            if (!File.Exists(path))
                missing.Add(display);
        }

        async Task CheckAssetsAsync(string indexPath, CancellationToken token)
        {
            try
            {
                await using var stream = File.OpenRead(indexPath);
                var index = await JsonSerializer.DeserializeAsync<AssetIndexFile>(
                    stream, JsonDefaults.Options, token);

                if (index?.Objects is not { Count: > 0 } objects)
                    return;

                foreach (var hash in objects.Keys)
                {
                    if (hash.Length < 2)
                        continue;

                    Check(
                        Path.Combine(_paths.AssetsDirectory, "objects", hash[..2], hash),
                        "assets/" + hash);
                }
            }
            catch (Exception)
            {
                missing.Add($"资源索引：{Path.GetFileNameWithoutExtension(indexPath)}");
            }
        }
    }

    public void Delete(InstalledVersion version)
    {        var root = Path.GetFullPath(_paths.VersionsDirectory);
        var target = Path.GetFullPath(version.Directory);

        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("版本目录不在 versions 目录内，已拒绝删除。");

        if (Directory.Exists(target))
            Directory.Delete(target, true);
    }

    /// <summary>把整个实例目录复制为新的版本 ID，并同步内部 id 字段。</summary>
    public InstalledVersion Duplicate(InstalledVersion source)
    {
        var root = Path.GetFullPath(_paths.VersionsDirectory);
        var sourceDirectory = Path.GetFullPath(source.Directory);

        if (!sourceDirectory.StartsWith(
                root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("源版本目录不在 versions 目录内，已拒绝复制。");
        }

        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException("源版本目录不存在。");

        var id = CreateCopyId(source.Id);
        var targetDirectory = _paths.VersionDirectory(id);
        CopyDirectory(sourceDirectory, targetDirectory);

        var newJson = Path.Combine(targetDirectory, id + ".json");
        var oldJson = Path.Combine(targetDirectory, source.Id + ".json");
        if (File.Exists(oldJson))
            File.Move(oldJson, newJson);

        var newJar = Path.Combine(targetDirectory, id + ".jar");
        var oldJar = Path.Combine(targetDirectory, source.Id + ".jar");
        if (File.Exists(oldJar))
            File.Move(oldJar, newJar);

        TryRewriteId(newJson, id);

        return new InstalledVersion(
            id,
            targetDirectory,
            newJson,
            File.Exists(newJar) ? newJar : null,
            source.Type,
            source.InheritsFrom);
    }

    private string CreateCopyId(string sourceId)
    {
        for (var index = 1; ; index++)
        {
            var id = index == 1 ? $"{sourceId}-copy" : $"{sourceId}-copy{index}";
            if (!Directory.Exists(_paths.VersionDirectory(id)) &&
                !File.Exists(_paths.VersionJsonPath(id)))
            {
                return id;
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static void TryRewriteId(string jsonPath, string id)
    {
        try
        {
            if (!File.Exists(jsonPath))
                return;

            if (JsonNode.Parse(File.ReadAllText(jsonPath)) is not JsonObject profile)
                return;

            profile["id"] = id;
            File.WriteAllText(
                jsonPath,
                profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or InvalidOperationException)
        {
            // id 字段无法改写时仍保留可启动的目录副本。
        }
    }
}
