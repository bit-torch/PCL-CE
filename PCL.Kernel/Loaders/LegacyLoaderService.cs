using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using PCL.Kernel.Install;
using PCL.Kernel.Net;

namespace PCL.Kernel.Loaders;

/// <summary>
/// OptiFine 与 LiteLoader 的安装。
/// OptiFine 通过官方安装器的 Installer 主类写入版本；LiteLoader 从官方清单构造版本 JSON。
/// </summary>
public sealed class LegacyLoaderService
{
    private const string OptiFineBase = "https://bmclapi2.bangbang93.com/optifine";
    private const string LiteLoaderManifestUrl = "https://dl.liteloader.com/versions/versions.json";
    private const string LiteLoaderMaven = "https://dl.liteloader.com/versions/";

    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;

    public LegacyLoaderService(LauncherPaths paths, DownloadClient downloads)
    {
        _paths = paths;
        _downloads = downloads;
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetOptiFineVersionsAsync(
        string gameVersion,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var cache = Path.Combine(
            _paths.CacheDirectory, $"optifine-{Sanitize(gameVersion)}.json");
        await EnsureFreshAsync(
                $"{OptiFineBase}/{Uri.EscapeDataString(gameVersion)}",
                cache, forceRefresh, cancellationToken)
            .ConfigureAwait(false);

        if (JsonNode.Parse(await File.ReadAllTextAsync(cache, cancellationToken)
                .ConfigureAwait(false)) is not JsonArray entries)
            return [];

        var result = new List<LoaderVersion>();
        foreach (var node in entries)
        {
            var type = node?["type"]?.ToString();
            var patch = node?["patch"]?.ToString();
            var file = node?["filename"]?.ToString();
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(patch))
                continue;

            result.Add(new LoaderVersion(
                $"{type}/{patch}",
                !(file?.Contains("preview", StringComparison.OrdinalIgnoreCase) ?? false),
                file?.Replace(".jar", "", StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    public async Task<LoaderInstallResult> InstallOptiFineAsync(
        string gameVersion,
        string loaderVersion,
        string javaPath,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parts = loaderVersion.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException("OptiFine 版本格式无效。", nameof(loaderVersion));

        if (!File.Exists(_paths.VersionJsonPath(gameVersion)))
            throw new InvalidOperationException($"请先安装原版 {gameVersion}。");
        if (!File.Exists(javaPath))
            throw new FileNotFoundException($"找不到 Java 可执行文件：{javaPath}");

        var (type, patch) = (parts[0], parts[1]);
        var installer = Path.Combine(
            _paths.CacheDirectory, "loaders",
            $"optifine-{Sanitize(gameVersion)}-{Sanitize(type)}-{Sanitize(patch)}.jar");

        progress?.Report(new InstallProgress("下载 OptiFine 安装器", 0, 1, 0));
        await _downloads.DownloadAsync(
                $"{OptiFineBase}/{Uri.EscapeDataString(gameVersion)}/" +
                $"{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(patch)}",
                installer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Directory.CreateDirectory(_paths.GameDirectory);
        var started = DateTime.UtcNow;

        progress?.Report(new InstallProgress("运行 OptiFine 安装器", 0, 1, 0.5));
        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = _paths.GameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add($"-Duser.home={_paths.GameDirectory}");
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(installer);
        startInfo.ArgumentList.Add("optifine.Installer");
        startInfo.Environment["APPDATA"] = _paths.GameDirectory;

        var output = await RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
        var id = FindInstalledVersion("OptiFine", started)
                 ?? throw new InvalidDataException(
                     $"OptiFine 安装器未生成版本 JSON。输出：{Tail(output)}");

        progress?.Report(new InstallProgress("OptiFine 安装完成", 1, 1, 1));
        return new LoaderInstallResult(id, gameVersion, CountLibraries(id));
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetLiteLoaderVersionsAsync(
        string gameVersion,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var manifest = await GetLiteLoaderManifestAsync(forceRefresh, cancellationToken)
            .ConfigureAwait(false);
        var entry = FindLiteLoaderEntry(manifest, gameVersion, null);
        if (entry is null)
            return [];

        var version = entry["version"]?.ToString() ?? "";
        if (version.Length == 0)
            return [];

        return
        [
            new LoaderVersion(
                version,
                !string.Equals(entry["stream"]?.ToString(), "snapshot",
                    StringComparison.OrdinalIgnoreCase),
                $"LiteLoader {version}")
        ];
    }

    public async Task<LoaderInstallResult> InstallLiteLoaderAsync(
        string gameVersion,
        string loaderVersion,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.VersionJsonPath(gameVersion)))
            throw new InvalidOperationException($"请先安装原版 {gameVersion}。");

        progress?.Report(new InstallProgress("读取 LiteLoader 清单", 0, 1, 0));
        var manifest = await GetLiteLoaderManifestAsync(true, cancellationToken)
            .ConfigureAwait(false);
        var entry = FindLiteLoaderEntry(manifest, gameVersion, loaderVersion)
                    ?? throw new InvalidDataException(
                        $"LiteLoader 清单中不存在 {gameVersion} 的 {loaderVersion}。");

        var tweakClass = entry["tweakClass"]?.ToString()
                         ?? "com.mumfrey.liteloader.launch.LiteLoaderTweaker";
        var id = $"{gameVersion}-LiteLoader";
        var directory = _paths.VersionDirectory(id);
        Directory.CreateDirectory(directory);

        var libraries = new JsonArray();
        if (entry["libraries"] is JsonArray provided)
        {
            foreach (var library in provided)
            {
                if (library is JsonObject clone)
                    libraries.Add(clone.DeepClone());
            }
        }

        libraries.Add(new JsonObject
        {
            ["name"] = $"com.mumfrey:liteloader:{loaderVersion}",
            ["url"] = LiteLoaderMaven
        });

        EnsureLibrary(libraries, "net.minecraft:launchwrapper:1.12",
            "https://libraries.minecraft.net/");

        NormalizeLibraries(libraries);

        var versionJson = new JsonObject
        {
            ["id"] = id,
            ["inheritsFrom"] = gameVersion,
            ["jar"] = gameVersion,
            ["type"] = "release",
            ["mainClass"] = "net.minecraft.launchwrapper.Launch",
            ["minimumLauncherVersion"] = 18,
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray("--tweakClass", tweakClass)
            },
            ["libraries"] = libraries
        };

        var jsonPath = Path.Combine(directory, id + ".json");
        await File.WriteAllTextAsync(jsonPath, versionJson.ToJsonString(),
                cancellationToken)
            .ConfigureAwait(false);

        var count = await DownloadLibrariesAsync(libraries, progress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new InstallProgress("LiteLoader 安装完成", 1, 1, 1));
        return new LoaderInstallResult(id, gameVersion, count);
    }

    private async Task<JsonObject> GetLiteLoaderManifestAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cache = Path.Combine(_paths.CacheDirectory, "liteloader-versions.json");
        await EnsureFreshAsync(LiteLoaderManifestUrl, cache, forceRefresh, cancellationToken)
            .ConfigureAwait(false);

        return JsonNode.Parse(await File.ReadAllTextAsync(cache, cancellationToken)
                   .ConfigureAwait(false)) as JsonObject
               ?? throw new InvalidDataException("无法解析 LiteLoader 版本清单。");
    }

    private static JsonObject? FindLiteLoaderEntry(
        JsonObject manifest,
        string gameVersion,
        string? loaderVersion)
    {
        var versionNode = manifest["versions"]?[gameVersion];
        var latest = versionNode?["artefacts"]?["com.mumfrey:liteloader"]?["latest"]
                     ?? versionNode?["snapshots"]?["com.mumfrey:liteloader"]?["latest"];
        if (latest is not JsonObject entry)
            return null;

        if (loaderVersion is not null &&
            !string.Equals(entry["version"]?.ToString(), loaderVersion,
                StringComparison.Ordinal))
            return null;

        return entry;
    }

    private async Task<int> DownloadLibrariesAsync(
        JsonArray libraries,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<(string Url, string Path)>();
        foreach (var library in libraries.OfType<JsonObject>())
        {
            var artifact = library["downloads"]?["artifact"];
            var path = artifact?["path"]?.ToString();
            var url = artifact?["url"]?.ToString();
            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(url))
                artifacts.Add((url!, path!));
        }

        var completed = 0;
        foreach (var artifact in artifacts)
        {
            var destination = Path.Combine(_paths.LibrariesDirectory,
                artifact.Path.Replace('/', Path.DirectorySeparatorChar));
            await _downloads.DownloadAsync(artifact.Url, destination,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            completed++;
            progress?.Report(new InstallProgress(
                "下载加载器依赖", completed, artifacts.Count,
                artifacts.Count == 0 ? 1 : (double)completed / artifacts.Count));
        }

        return artifacts.Count;
    }

    /// <summary>为只有 Maven 坐标与仓库地址的库补全 downloads.artifact 信息。</summary>
    private static void EnsureLibrary(JsonArray libraries, string coordinate, string repository)
    {
        foreach (var library in libraries.OfType<JsonObject>())
        {
            if (string.Equals(library["name"]?.ToString(), coordinate, StringComparison.Ordinal))
                return;
        }

        var path = MavenPathFromCoordinate(coordinate);
        libraries.Add(new JsonObject
        {
            ["name"] = coordinate,
            ["url"] = repository,
            ["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject
                {
                    ["path"] = path,
                    ["url"] = repository + path
                }
            }
        });
    }

    private void NormalizeLibraries(JsonArray libraries)
    {
        foreach (var library in libraries.OfType<JsonObject>())
        {
            if (library["downloads"]?["artifact"] is not null)
                continue;

            var name = library["name"]?.ToString();
            var repository = library["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(repository))
                continue;

            var path = MavenPathFromCoordinate(name);
            library["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject
                {
                    ["path"] = path,
                    ["url"] = repository.TrimEnd('/') + "/" + path
                }
            };
        }
    }

    private static string MavenPathFromCoordinate(string coordinate)
    {
        var parts = coordinate.Split(':');
        if (parts.Length < 3)
            throw new ArgumentException($"无效的 Maven 坐标：{coordinate}", nameof(coordinate));

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }

    private string? FindInstalledVersion(string keyword, DateTime started)
    {
        if (!Directory.Exists(_paths.VersionsDirectory))
            return null;

        return Directory.EnumerateDirectories(_paths.VersionsDirectory)
            .Select(Path.GetFileName)
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                         id!.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Where(id =>
            {
                var json = _paths.VersionJsonPath(id!);
                return File.Exists(json) && File.GetLastWriteTimeUtc(json) >= started.AddSeconds(-5);
            })
            .OrderByDescending(id => File.GetLastWriteTimeUtc(_paths.VersionJsonPath(id!)))
            .FirstOrDefault();
    }

    private int CountLibraries(string versionId)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(_paths.VersionJsonPath(versionId)));
            return json?["libraries"] is JsonArray array ? array.Count : 0;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    private async Task EnsureFreshAsync(
        string url,
        string cache,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var fresh = !forceRefresh &&
                    File.Exists(cache) &&
                    File.GetLastWriteTimeUtc(cache) > DateTime.UtcNow.AddHours(-6);
        if (!fresh)
            await _downloads.DownloadAsync(url, cache, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
    }

    private static async Task<string> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        var buffer = new StringBuilder();

        process.OutputDataReceived += (_, args) => AppendLine(buffer, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(buffer, args.Data);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("无法启动安装器。");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"安装器退出码 {process.ExitCode}：{Tail(buffer.ToString())}");

        return buffer.ToString();
    }

    private static void AppendLine(StringBuilder buffer, string? line)
    {
        if (line is null)
            return;

        lock (buffer)
            buffer.AppendLine(line);
    }

    private static string Tail(string text)
    {
        const int limit = 1200;
        return text.Length <= limit ? text.Trim() : text[^limit..].Trim();
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
}
