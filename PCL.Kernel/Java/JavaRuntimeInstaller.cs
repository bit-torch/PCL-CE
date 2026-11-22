using System.Text.Json;
using PCL.Kernel.Install;
using PCL.Kernel.Mojang;
using PCL.Kernel.Net;

namespace PCL.Kernel.Java;

/// <summary>
/// 下载并解压 Mojang 官方 Java 运行时。文件都会做 SHA-1 校验，可重复执行。
/// </summary>
public sealed class JavaRuntimeInstaller
{
    private const int MaxConcurrentDownloads = 6;

    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;
    private readonly JavaRuntimeService _runtimes;

    public JavaRuntimeInstaller(
        LauncherPaths paths,
        DownloadClient downloads,
        JavaRuntimeService runtimes)
    {
        _paths = paths;
        _downloads = downloads;
        _runtimes = runtimes;
    }

    public string DirectoryFor(JavaRuntimePackage package) =>
        Path.Combine(_paths.RuntimesDirectory, $"{package.Component}-{package.Platform}");

    /// <summary>在运行时目录中寻找 java 可执行文件。</summary>
    public static string? FindExecutable(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        var candidates = OperatingSystem.IsWindows()
            ? new[] { "bin/java.exe" }
            : new[]
            {
                "bin/java",
                "jre.bundle/Contents/Home/bin/java",
                "Contents/Home/bin/java"
            };

        foreach (var relative in candidates)
        {
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                return path;
        }

        var name = OperatingSystem.IsWindows() ? "java.exe" : "java";
        try
        {
            return Directory
                .EnumerateFiles(directory, name, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task<string> InstallAsync(
        string? component = null,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var package = await _runtimes
            .GetPackageAsync(component, cancellationToken)
            .ConfigureAwait(false);

        var targetDirectory = DirectoryFor(package);
        var existing = FindExecutable(targetDirectory);
        if (existing is not null)
        {
            progress?.Report(new InstallProgress("Java 运行时已就绪", 1, 1, 1));
            return existing;
        }

        progress?.Report(new InstallProgress("读取 Java 运行时清单", 0, 1, 0));

        var manifestPath = Path.Combine(
            _paths.CacheDirectory,
            $"java-runtime-{package.Component}-{package.Platform}.json");

        await _downloads.DownloadAsync(
                package.ManifestUrl,
                manifestPath,
                package.ManifestSha1,
                package.ManifestSize,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var json = await File
            .ReadAllTextAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);

        var manifest = JsonSerializer.Deserialize<JavaRuntimeFileManifest>(json, JsonDefaults.Options)
                       ?? throw new InvalidDataException("无法解析 Java 运行时文件清单。");

        var entries = manifest.Files
            .Where(pair => pair.Value.Type is "file" or "directory" or "link")
            .ToList();

        var total = entries.Count;
        var completed = 0;

        using var gate = new SemaphoreSlim(MaxConcurrentDownloads);

        var tasks = entries.Select(async pair =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await InstallEntryAsync(targetDirectory, pair.Key, pair.Value, cancellationToken)
                    .ConfigureAwait(false);

                var index = Interlocked.Increment(ref completed);
                var fraction = total == 0 ? 1 : (double)index / total;
                progress?.Report(new InstallProgress(
                    "安装 Java 运行时", index, total, fraction));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return FindExecutable(targetDirectory)
               ?? throw new InvalidOperationException(
                   "Java 运行时安装完成，但未找到 java 可执行文件。");
    }

    private async Task InstallEntryAsync(
        string root,
        string relativePath,
        JavaRuntimeFile entry,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(root, relativePath);

        switch (entry.Type)
        {
            case "directory":
                Directory.CreateDirectory(path);
                return;

            case "link":
                if (string.IsNullOrWhiteSpace(entry.Target) ||
                    File.Exists(path) || Directory.Exists(path))
                    return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.CreateSymbolicLink(path, entry.Target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or PlatformNotSupportedException)
                {
                    // 无权限创建链接时跳过，运行时仍可通过其他文件正常工作。
                }

                return;

            default:
                if (entry.Downloads is null ||
                    !entry.Downloads.TryGetValue("raw", out var artifact) ||
                    artifact is null ||
                    string.IsNullOrWhiteSpace(artifact.Url))
                    return;

                await _downloads.DownloadAsync(
                        artifact.Url,
                        path,
                        artifact.Sha1,
                        artifact.Size,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (entry.Executable)
                    MakeExecutable(path);

                return;
        }
    }

    private static string ResolvePath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var combined = Path.GetFullPath(
            Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(combined, fullRoot, StringComparison.Ordinal))
            throw new InvalidDataException($"Java 运行时清单包含非法路径：{relativePath}");

        return combined;
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode |
                                   UnixFileMode.UserExecute |
                                   UnixFileMode.GroupExecute |
                                   UnixFileMode.OtherExecute);
    }
}
