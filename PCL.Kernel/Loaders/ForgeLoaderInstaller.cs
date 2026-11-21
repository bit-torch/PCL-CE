using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PCL.Kernel.Install;
using PCL.Kernel.Mojang;
using PCL.Kernel.Net;

namespace PCL.Kernel.Loaders;

/// <summary>
/// Forge / NeoForge 安装：从官方 Maven 读取版本，下载安装器并在游戏目录内以客户端模式执行。
/// 安装器会自行写入版本 JSON、依赖库与处理后的客户端文件。
/// </summary>
public sealed class ForgeLoaderInstaller
{
    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;

    public ForgeLoaderInstaller(LauncherPaths paths, DownloadClient downloads)
    {
        _paths = paths;
        _downloads = downloads;
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetVersionsAsync(
        LoaderKind kind,
        string gameVersion,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        RequireForgeKind(kind);

        var url = kind == LoaderKind.Forge
            ? LoaderMetaEndpoints.ForgeMavenMetadataUrl
            : LoaderMetaEndpoints.NeoForgeMavenMetadataUrl;
        var cachePath = Path.Combine(
            _paths.CacheDirectory,
            $"loader-maven-{kind.ToString().ToLowerInvariant()}.xml");

        var fresh = !forceRefresh &&
                    File.Exists(cachePath) &&
                    File.GetLastWriteTimeUtc(cachePath) > DateTime.UtcNow.AddHours(-6);
        if (!fresh)
            await _downloads.DownloadAsync(url, cachePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var xml = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
        return ParseMavenVersions(kind, gameVersion, xml);
    }

    /// <summary>解析 Maven 元数据中的版本列表，并按当前游戏版本过滤。</summary>
    public static IReadOnlyList<LoaderVersion> ParseMavenVersions(
        LoaderKind kind,
        string gameVersion,
        string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return [];
        }

        var result = new List<LoaderVersion>();

        foreach (var entry in document.Descendants("version"))
        {
            var version = entry.Value.Trim();
            if (version.Length == 0)
                continue;

            if (kind == LoaderKind.Forge)
            {
                var prefix = gameVersion + "-";
                if (!version.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var loader = version[prefix.Length..];
                if (loader.Length > 0)
                    result.Add(new LoaderVersion(loader, !loader.Contains('-')));
            }
            else if (kind == LoaderKind.NeoForge)
            {
                if (string.Equals(NeoForgeMinecraftVersion(version), gameVersion,
                        StringComparison.Ordinal))
                    result.Add(new LoaderVersion(version, !version.Contains('-')));
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        result.Sort(static (left, right) =>
            CompareNumericDescending(left.Version, right.Version));
        return result;
    }

    /// <summary>把 NeoForge 版本号映射回 Minecraft 版本，例如 21.1.80 → 1.21.1。</summary>
    public static string? NeoForgeMinecraftVersion(string neoforgeVersion)
    {
        var core = neoforgeVersion.Split('-')[0];
        var parts = core.Split('.');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor))
            return null;

        // net.neoforged:neoforge 只覆盖 1.20.2 及以后；更早版本使用另一个构件。
        if (major < 20 || (major == 20 && minor < 2))
            return null;

        return minor == 0 ? $"1.{major}" : $"1.{major}.{minor}";
    }

    public async Task<LoaderInstallResult> InstallAsync(
        LoaderKind kind,
        string baseVersionId,
        string loaderVersion,
        string javaPath,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireForgeKind(kind);

        if (!File.Exists(_paths.VersionJsonPath(baseVersionId)))
            throw new InvalidOperationException(
                $"请先安装原版 {baseVersionId}，再安装 {kind} 加载器。");
        if (!File.Exists(javaPath))
            throw new FileNotFoundException($"找不到 Java 可执行文件：{javaPath}");

        var expectedId = kind == LoaderKind.Forge
            ? LoaderMetaEndpoints.ForgeVersionId(baseVersionId, loaderVersion)
            : LoaderMetaEndpoints.NeoForgeVersionId(loaderVersion);

        var existingJson = _paths.VersionJsonPath(expectedId);
        if (File.Exists(existingJson))
            return new LoaderInstallResult(expectedId, baseVersionId, CountLibraries(existingJson));

        var installerUrl = kind == LoaderKind.Forge
            ? LoaderMetaEndpoints.ForgeInstallerUrl(baseVersionId, loaderVersion)
            : LoaderMetaEndpoints.NeoForgeInstallerUrl(loaderVersion);
        var installerPath = Path.Combine(
            _paths.CacheDirectory,
            "loaders",
            $"{kind.ToString().ToLowerInvariant()}-{Sanitize(expectedId)}-installer.jar");

        progress?.Report(new InstallProgress("下载加载器安装器", 0, 1, 0));
        await _downloads.DownloadAsync(installerUrl, installerPath,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Directory.CreateDirectory(_paths.GameDirectory);
        EnsureLauncherProfiles();

        progress?.Report(new InstallProgress("运行加载器安装器", 0, 1, 0.5));

        var output = await RunInstallerAsync(javaPath, installerPath, cancellationToken)
            .ConfigureAwait(false);

        var installedId = ResolveInstalledId(expectedId, kind);
        progress?.Report(new InstallProgress("加载器安装完成", 1, 1, 1));

        return new LoaderInstallResult(
            installedId,
            baseVersionId,
            CountLibraries(_paths.VersionJsonPath(installedId)));
    }

    private async Task<string> RunInstallerAsync(
        string javaPath,
        string installerPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = _paths.GameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("--installClient");

        using var process = new Process { StartInfo = startInfo };
        var buffer = new StringBuilder();

        process.OutputDataReceived += (_, args) => AppendLine(buffer, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(buffer, args.Data);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("无法启动加载器安装器。");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"加载器安装器退出码 {process.ExitCode}：{Tail(buffer.ToString())}");

        return buffer.ToString();
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

    private string ResolveInstalledId(string expectedId, LoaderKind kind)
    {
        if (File.Exists(_paths.VersionJsonPath(expectedId)))
            return expectedId;

        if (Directory.Exists(_paths.VersionsDirectory))
        {
            var candidate = Directory.EnumerateDirectories(_paths.VersionsDirectory)
                .Select(Path.GetFileName)
                .Where(id => !string.IsNullOrWhiteSpace(id) && MatchesKind(id!, kind))
                .Where(id => File.Exists(_paths.VersionJsonPath(id!)))
                .OrderByDescending(id => File.GetLastWriteTimeUtc(_paths.VersionJsonPath(id!)))
                .FirstOrDefault();

            if (candidate is not null)
                return candidate;
        }

        throw new InvalidDataException(
            $"加载器安装完成，但未找到版本 JSON（预期 {expectedId}）。");
    }

    private static bool MatchesKind(string id, LoaderKind kind) =>
        kind == LoaderKind.Forge
            ? id.Contains("-forge-", StringComparison.OrdinalIgnoreCase)
            : id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase);

    private static int CountLibraries(string jsonPath)
    {
        try
        {
            var detail = JsonSerializer.Deserialize<VersionDetail>(
                File.ReadAllText(jsonPath), JsonDefaults.Options);
            return detail?.Libraries?.Count ?? 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return 0;
        }
    }

    private static void RequireForgeKind(LoaderKind kind)
    {
        if (kind is not (LoaderKind.Forge or LoaderKind.NeoForge))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "仅支持 Forge 或 NeoForge。");
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static int CompareNumericDescending(string left, string right)
    {
        var leftParts = left.Split('-')[0].Split('.');
        var rightParts = right.Split('-')[0].Split('.');

        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            var leftValue = index < leftParts.Length && int.TryParse(leftParts[index], out var l) ? l : 0;
            var rightValue = index < rightParts.Length && int.TryParse(rightParts[index], out var r) ? r : 0;

            if (leftValue != rightValue)
                return rightValue.CompareTo(leftValue);
        }

        return string.CompareOrdinal(right, left);
    }
}
