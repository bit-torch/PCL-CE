using System.IO.Compression;
using System.Text.Json.Nodes;
using PCL.Kernel.Install;
using PCL.Kernel.Net;

namespace PCL.Kernel.Modpacks;

/// <summary>
/// 安装 Modrinth 格式（.mrpack）整合包。只读取公开的 modrinth.index.json，
/// 下载清单中的文件并解压 overrides / client-overrides 到游戏目录。
/// </summary>
public sealed class MrPackInstaller
{
    private const string ManifestEntry = "modrinth.index.json";
    private const string OverridesPrefix = "overrides/";
    private const string ClientOverridesPrefix = "client-overrides/";

    private readonly string _gameDirectory;
    private readonly DownloadClient _downloads;

    public MrPackInstaller(string gameDirectory, DownloadClient downloads)
    {
        _gameDirectory = Path.GetFullPath(gameDirectory);
        _downloads = downloads;
    }

    public string GameDirectory => _gameDirectory;

    /// <summary>读取 .mrpack 中的整合包清单，不执行任何下载。</summary>
    public static ModpackManifest ReadManifest(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(ManifestEntry)
                    ?? throw new InvalidDataException("整合包缺少 modrinth.index.json。");

        using var stream = entry.Open();
        var node = JsonNode.Parse(stream) as JsonObject
                   ?? throw new InvalidDataException("整合包清单不是有效的 JSON 对象。");

        if (node["game"]?.ToString() is { Length: > 0 } game &&
            !string.Equals(game, "minecraft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"不支持的整合包类型：{game}");

        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node["dependencies"] is JsonObject dependencyNode)
        {
            foreach (var (key, value) in dependencyNode)
            {
                if (value?.ToString() is { Length: > 0 } version)
                    dependencies[key] = version;
            }
        }

        var files = new List<ModpackFile>();
        if (node["files"] is JsonArray fileNodes)
        {
            foreach (var item in fileNodes.OfType<JsonObject>())
            {
                var path = item["path"]?.ToString();
                var url = (item["downloads"] as JsonArray)?
                    .Select(candidate => candidate?.ToString())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url))
                    continue;

                files.Add(new ModpackFile(
                    path,
                    url,
                    item["hashes"]?["sha1"]?.ToString(),
                    item["hashes"]?["sha512"]?.ToString(),
                    item["fileSize"]?.GetValue<long>() ?? 0,
                    item["env"]?["client"]?.ToString() ?? "required"));
            }
        }

        return new ModpackManifest(
            node["name"]?.ToString() ?? Path.GetFileNameWithoutExtension(archivePath),
            node["versionId"]?.ToString() ?? "",
            string.IsNullOrWhiteSpace(node["summary"]?.ToString())
                ? null
                : node["summary"]!.ToString(),
            dependencies.GetValueOrDefault("minecraft"),
            dependencies,
            files);
    }

    /// <summary>下载清单文件并解压覆盖目录。所有路径都会限制在游戏目录内。</summary>
    public async Task<ModpackInstallResult> InstallAsync(
        string archivePath,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = ReadManifest(archivePath);
        Directory.CreateDirectory(_gameDirectory);

        var installed = new List<string>();
        var targets = manifest.Files.Where(file => !file.SkipOnClient).ToArray();

        for (var index = 0; index < targets.Length; index++)
        {
            var file = targets[index];
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new InstallProgress(
                $"下载 {Path.GetFileName(file.Path)}",
                index,
                targets.Length,
                targets.Length == 0 ? 0 : (double)index / targets.Length));

            var destination = SafeCombine(file.Path);
            await _downloads.DownloadAsync(
                    file.DownloadUrl,
                    destination,
                    sha1: file.Sha1,
                    size: file.Size > 0 ? file.Size : null,
                    cancellationToken: cancellationToken,
                    sha512: file.Sha512)
                .ConfigureAwait(false);

            installed.Add(file.Path);
        }

        progress?.Report(new InstallProgress(
            "解压覆盖文件", targets.Length, targets.Length, 1));
        var overrides = ExtractOverrides(archivePath);

        progress?.Report(new InstallProgress(
            "整合包安装完成", targets.Length, targets.Length, 1));
        return new ModpackInstallResult(
            manifest.Name, manifest.MinecraftVersion, installed, overrides);
    }

    private IReadOnlyList<string> ExtractOverrides(string archivePath)
    {
        var extracted = new List<string>();

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var relative = RelativeOverridePath(entry.FullName);
            if (relative is null)
                continue;

            var destination = SafeCombine(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
            extracted.Add(relative);
        }

        return extracted;
    }

    private static string? RelativeOverridePath(string entryName)
    {
        string? relative = null;

        if (entryName.StartsWith(OverridesPrefix, StringComparison.Ordinal))
            relative = entryName[OverridesPrefix.Length..];
        else if (entryName.StartsWith(ClientOverridesPrefix, StringComparison.Ordinal))
            relative = entryName[ClientOverridesPrefix.Length..];

        if (string.IsNullOrWhiteSpace(relative) ||
            relative.EndsWith('/') ||
            relative.EndsWith('\\'))
            return null;

        return relative;
    }

    private string SafeCombine(string relativePath)
    {
        var normalized = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_gameDirectory, normalized));
        var relative = Path.GetRelativePath(_gameDirectory, fullPath);

        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException($"整合包包含非法路径：{relativePath}");

        return fullPath;
    }
}
