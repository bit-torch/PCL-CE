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
    private const string LabyModManifestUrl =
        "https://releases.r2.labymod.net/api/v1/manifest/production/latest.json";
    private const string CleanroomReleasesUrl =
        "https://api.github.com/repos/CleanroomMC/Cleanroom/releases";

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
        var javac = Path.Combine(
            Path.GetDirectoryName(javaPath)!,
            OperatingSystem.IsWindows() ? "javac.exe" : "javac");
        if (!File.Exists(javac))
            throw new InvalidOperationException(
                "OptiFine 安装需要 JDK（javac），请安装或指定 JDK 而不是仅 JRE。");

        var work = Path.Combine(Path.GetTempPath(), "PCLCE",
            "optifine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var previous = Directory.Exists(_paths.VersionsDirectory)
            ? Directory.GetDirectories(_paths.VersionsDirectory).ToHashSet()
            : [];

        try
        {
            var source = Path.Combine(work, "PclOptiFineBridge.java");
            await File.WriteAllTextAsync(source,
                    "public class PclOptiFineBridge { public static void main(String[] args) " +
                    "throws Exception { java.lang.reflect.Method m = " +
                    "Class.forName(\"optifine.Installer\").getDeclaredMethod(\"doInstall\", " +
                    "java.io.File.class); m.setAccessible(true); m.invoke(null, " +
                    "new java.io.File(args[0])); } }",
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new InstallProgress("编译 OptiFine 安装桥", 0, 1, 0.5));
            var compile = new ProcessStartInfo
            {
                FileName = javac,
                WorkingDirectory = work,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            compile.ArgumentList.Add("-source");
            compile.ArgumentList.Add("8");
            compile.ArgumentList.Add("-target");
            compile.ArgumentList.Add("8");
            compile.ArgumentList.Add("-d");
            compile.ArgumentList.Add(work);
            compile.ArgumentList.Add(source);
            await RunAsync(compile, cancellationToken).ConfigureAwait(false);

            progress?.Report(new InstallProgress("运行 OptiFine 安装器", 0, 1, 0.7));
            var run = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = _paths.GameDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            run.ArgumentList.Add($"-Duser.home={_paths.GameDirectory}");
            run.ArgumentList.Add("-cp");
            run.ArgumentList.Add(string.Join(Path.PathSeparator, work, installer));
            run.ArgumentList.Add("PclOptiFineBridge");
            run.ArgumentList.Add(_paths.GameDirectory);
            run.Environment["APPDATA"] = _paths.GameDirectory;

            var output = await RunAsync(run, cancellationToken).ConfigureAwait(false);

            var added = Directory.Exists(_paths.VersionsDirectory)
                ? Directory.GetDirectories(_paths.VersionsDirectory)
                    .FirstOrDefault(path => !previous.Contains(path))
                : null;
            var id = added is not null
                ? Path.GetFileName(added)
                : FindInstalledVersion("OptiFine", DateTime.UtcNow.AddMinutes(-2));

            if (id is null)
                throw new InvalidDataException(
                    $"OptiFine 安装器未生成版本 JSON。输出：{Tail(output)}");

            progress?.Report(new InstallProgress("OptiFine 安装完成", 1, 1, 1));
            return new LoaderInstallResult(id, gameVersion, CountLibraries(id));
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch (IOException)
            {
            }
        }
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

    public async Task<IReadOnlyList<LoaderVersion>> GetLabyModVersionsAsync(
        string gameVersion,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var manifest = await GetCachedJsonAsync(
                LabyModManifestUrl, "labymod-latest.json", forceRefresh, cancellationToken)
            .ConfigureAwait(false);

        if (manifest?["minecraftVersions"] is not JsonArray versions ||
            versions.FirstOrDefault(item =>
                string.Equals(item?["version"]?.ToString(), gameVersion,
                    StringComparison.Ordinal)) is null)
            return [];

        var commit = manifest["commitReference"]?.ToString()
                     ?? manifest["commit_reference"]?.ToString() ?? "";
        var version = manifest["labyModVersion"]?.ToString() ?? commit;
        if (version.Length == 0)
            return [];

        return [new LoaderVersion(version, true, $"LabyMod {version}")];
    }

    public async Task<LoaderInstallResult> InstallLabyModAsync(
        string gameVersion,
        string loaderVersion,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.VersionJsonPath(gameVersion)))
            throw new InvalidOperationException($"请先安装原版 {gameVersion}。");

        progress?.Report(new InstallProgress("读取 LabyMod 清单", 0, 1, 0));
        var manifest = await GetCachedJsonAsync(
                LabyModManifestUrl, "labymod-latest.json", true, cancellationToken)
            .ConfigureAwait(false);

        if (manifest?["minecraftVersions"] is not JsonArray versions)
            throw new InvalidDataException("LabyMod 清单缺少版本信息。");

        var supported = versions.OfType<JsonObject>().FirstOrDefault(item =>
            string.Equals(item["version"]?.ToString(), gameVersion, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"LabyMod 不支持 Minecraft {gameVersion}。");

        var commit = manifest["commitReference"]?.ToString()
                     ?? manifest["commit_reference"]?.ToString() ?? "";
        var profileUrl = supported["customManifestUrl"]?.ToString()
                         ?? $"https://releases.r2.labymod.net/api/v1/download/manifest/" +
                            $"labymod4/production/{Uri.EscapeDataString(gameVersion)}/{commit}.json";

        var profile = await GetCachedJsonAsync(
                profileUrl,
                $"labymod-{Sanitize(gameVersion)}-{Sanitize(loaderVersion)}.json",
                true, cancellationToken)
            .ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException("无法解析 LabyMod 版本描述。");

        var id = profile["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("LabyMod 版本描述缺少 id。");

        if (File.Exists(_paths.VersionJsonPath(id)))
            return new LoaderInstallResult(id, gameVersion, CountLibraries(id));

        profile["inheritsFrom"] ??= gameVersion;
        var libraries = profile["libraries"] as JsonArray ?? new JsonArray();
        profile["libraries"] = libraries;
        NormalizeLibraries(libraries);

        Directory.CreateDirectory(_paths.VersionDirectory(id));
        await File.WriteAllTextAsync(_paths.VersionJsonPath(id), profile.ToJsonString(),
                cancellationToken)
            .ConfigureAwait(false);

        var count = await DownloadLibrariesAsync(libraries, progress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new InstallProgress("LabyMod 安装完成", 1, 1, 1));
        return new LoaderInstallResult(id, gameVersion, count);
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetCleanroomVersionsAsync(
        string gameVersion,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(gameVersion, "1.12.2", StringComparison.Ordinal))
            return [];

        var releases = await GetCachedJsonAsync(
                CleanroomReleasesUrl, "cleanroom-releases.json", forceRefresh,
                cancellationToken)
            .ConfigureAwait(false) as JsonArray
            ?? [];

        var result = new List<LoaderVersion>();
        foreach (var release in releases.OfType<JsonObject>())
        {
            var tag = release["tag_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(tag) || FindInstallerAsset(release) is null)
                continue;

            result.Add(new LoaderVersion(tag, true, $"Cleanroom {tag}"));
        }

        return result;
    }

    public async Task<LoaderInstallResult> InstallCleanroomAsync(
        string gameVersion,
        string loaderVersion,
        string javaPath,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(gameVersion, "1.12.2", StringComparison.Ordinal))
            throw new InvalidOperationException("Cleanroom 仅支持 Minecraft 1.12.2。");
        if (!File.Exists(_paths.VersionJsonPath(gameVersion)))
            throw new InvalidOperationException($"请先安装原版 {gameVersion}。");
        if (!File.Exists(javaPath))
            throw new FileNotFoundException($"找不到 Java 可执行文件：{javaPath}");

        var releases = await GetCachedJsonAsync(
                CleanroomReleasesUrl, "cleanroom-releases.json", true, cancellationToken)
            .ConfigureAwait(false) as JsonArray
            ?? throw new InvalidDataException("无法读取 Cleanroom 版本清单。");

        var release = releases.OfType<JsonObject>().FirstOrDefault(item =>
            string.Equals(item["tag_name"]?.ToString(), loaderVersion,
                StringComparison.Ordinal))
            ?? throw new InvalidDataException($"未找到 Cleanroom {loaderVersion}。");

        var downloadUrl = FindInstallerAsset(release)?["browser_download_url"]?.ToString()
                          ?? throw new InvalidDataException("Cleanroom 发布缺少安装器。");

        var installer = Path.Combine(_paths.CacheDirectory, "loaders",
            $"cleanroom-{Sanitize(loaderVersion)}-installer.jar");

        progress?.Report(new InstallProgress("下载 Cleanroom 安装器", 0, 1, 0));
        await _downloads.DownloadAsync(downloadUrl, installer,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Directory.CreateDirectory(_paths.GameDirectory);
        EnsureLauncherProfiles();
        var previous = Directory.Exists(_paths.VersionsDirectory)
            ? Directory.GetDirectories(_paths.VersionsDirectory).ToHashSet()
            : [];

        progress?.Report(new InstallProgress("运行 Cleanroom 安装器", 0, 1, 0.5));
        var run = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = _paths.GameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        run.ArgumentList.Add("-jar");
        run.ArgumentList.Add(installer);
        run.ArgumentList.Add("--installClient");

        var output = await RunAsync(run, cancellationToken).ConfigureAwait(false);
        var added = Directory.Exists(_paths.VersionsDirectory)
            ? Directory.GetDirectories(_paths.VersionsDirectory)
                .FirstOrDefault(path => !previous.Contains(path))
            : null;
        var id = added is not null
            ? Path.GetFileName(added)
            : FindInstalledVersion("cleanroom", DateTime.UtcNow.AddMinutes(-2));

        if (id is null)
            throw new InvalidDataException(
                $"Cleanroom 安装器未生成版本 JSON。输出：{Tail(output)}");

        progress?.Report(new InstallProgress("Cleanroom 安装完成", 1, 1, 1));
        return new LoaderInstallResult(id, gameVersion, CountLibraries(id));
    }

    private static JsonObject? FindInstallerAsset(JsonObject release) =>
        (release["assets"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(asset =>
            asset["name"]?.ToString()?.EndsWith("-installer.jar",
                StringComparison.OrdinalIgnoreCase) == true);

    private async Task<JsonNode?> GetCachedJsonAsync(
        string url,
        string cacheName,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cache = Path.Combine(_paths.CacheDirectory, cacheName);
        await EnsureFreshAsync(url, cache, forceRefresh, cancellationToken)
            .ConfigureAwait(false);

        return JsonNode.Parse(await File.ReadAllTextAsync(cache, cancellationToken)
            .ConfigureAwait(false));
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

    private void EnsureLauncherProfiles()
    {
        var path = Path.Combine(_paths.GameDirectory, "launcher_profiles.json");
        if (File.Exists(path))
            return;

        const string json =
            "{\"profiles\":{},\"selectedProfile\":\"\",\"clientToken\":\"\"," +
            "\"authenticationDatabase\":{},\"launcherVersion\":{\"name\":\"1.0\",\"format\":21}," +
            "\"settings\":{},\"version\":3}";
        File.WriteAllText(path, json);
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
