using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using PCL.Kernel.Net;

namespace PCL.Kernel.Updates;

/// <summary>已解压等待替换的更新。</summary>
public sealed record PreparedUpdate(
    string Version,
    string WorkspaceDirectory,
    string ContentDirectory,
    string AppDirectory);

/// <summary>
/// 下载并准备启动器更新：只负责下载、解压与生成替换脚本，实际替换由脚本在进程退出后执行。
/// </summary>
public sealed class UpdateInstaller
{
    private readonly DownloadClient _downloads;
    private readonly string _workDirectory;

    public UpdateInstaller(DownloadClient downloads, string workDirectory)
    {
        _downloads = downloads;
        _workDirectory = Path.GetFullPath(workDirectory);
    }

    public string WorkDirectory => _workDirectory;

    /// <summary>从更新信息提供的压缩包下载到工作目录，返回本地路径。</summary>
    public async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl) || !IsSupportedArchive(info.DownloadUrl))
            throw new InvalidOperationException("该更新没有可下载的压缩包，请打开发布页手动更新。");

        var fileName = Path.GetFileName(new Uri(info.DownloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "update.zip";

        var directory = Path.Combine(_workDirectory, Sanitize(info.LatestVersion));
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, Sanitize(fileName));
        await _downloads.DownloadAsync(
                info.DownloadUrl,
                destination,
                progress: progress,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return destination;
    }

    /// <summary>解压更新包并返回待替换的目录。更新包中的路径不会逃逸出临时目录。</summary>
    public PreparedUpdate Prepare(
        string archivePath,
        string appDirectory,
        string? version = null)
    {
        var workspace = Path.Combine(
            _workDirectory, "stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ExtractZip(archivePath, workspace);
            else
                ExtractTarGz(archivePath, workspace);

            var content = NormalizeRoot(workspace);
            return new PreparedUpdate(
                version ?? "unknown",
                workspace,
                content,
                Path.GetFullPath(appDirectory));
        }
        catch
        {
            SafeDelete(workspace);
            throw;
        }
    }

    /// <summary>
    /// 生成进程退出后执行的替换脚本。Windows 使用 .cmd，其余平台使用 .sh。
    /// </summary>
    public static string CreateApplyScript(
        PreparedUpdate prepared,
        int processId,
        string? relaunchPath = null)
    {
        var isWindows = OperatingSystem.IsWindows();
        var extension = isWindows ? ".cmd" : ".sh";
        var path = Path.Combine(
            prepared.WorkspaceDirectory + extension);

        var app = prepared.AppDirectory;
        var content = prepared.ContentDirectory;
        var workspace = prepared.WorkspaceDirectory;
        var relaunch = string.IsNullOrWhiteSpace(relaunchPath) ? app : relaunchPath;

        var script = isWindows
            ? BuildWindowsScript(processId, content, app, relaunch, workspace)
            : BuildUnixScript(processId, content, app, relaunch, workspace);

        File.WriteAllText(path, script, new UTF8Encoding(false));

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        return path;
    }

    public static void SafeDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // 文件被占用时交给系统下次清理。
        }
    }

    public static bool IsSupportedArchive(string urlOrName)
    {
        var name = urlOrName.Split('?', '#')[0];
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase);
    }

    private static void ExtractZip(string archivePath, string workspace)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destination = SafeCombine(workspace, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static void ExtractTarGz(string archivePath, string workspace)
    {
        using var file = File.OpenRead(archivePath);

        if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            using var tar = new TarReader(file);
            ExtractTar(tar, workspace);
            return;
        }

        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var gzipReader = new TarReader(gzip);
        ExtractTar(gzipReader, workspace);
    }

    private static void ExtractTar(TarReader reader, string workspace)
    {
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.Directory ||
                string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var destination = SafeCombine(workspace, entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static string NormalizeRoot(string workspace)
    {
        var entries = Directory.EnumerateFileSystemEntries(workspace).ToArray();
        if (entries.Length == 1 && Directory.Exists(entries[0]))
            return entries[0];

        return workspace;
    }

    private static string SafeCombine(string root, string relativePath)
    {
        var normalized = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        var relative = Path.GetRelativePath(root, fullPath);

        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException($"更新包包含非法路径：{relativePath}");

        return fullPath;
    }

    private static string BuildWindowsScript(
        int processId, string content, string app, string relaunch, string workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("setlocal");
        builder.AppendLine(":wait");
        builder.AppendLine($"tasklist /FI \"PID eq {processId}\" | find \"{processId}\" >nul");
        builder.AppendLine("if not errorlevel 1 (");
        builder.AppendLine("  timeout /t 1 /nobreak >nul");
        builder.AppendLine("  goto wait");
        builder.AppendLine(")");
        builder.AppendLine($"robocopy \"{content}\" \"{app}\" /E /NFL /NDL /NJH /NJS >nul");
        builder.AppendLine($"start \"\" \"{relaunch}\"");
        builder.AppendLine($"rmdir /s /q \"{workspace}\"");
        builder.AppendLine("endlocal");
        return builder.ToString();
    }

    private static string BuildUnixScript(
        int processId, string content, string app, string relaunch, string workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/sh");
        builder.AppendLine($"while kill -0 {processId} 2>/dev/null; do sleep 1; done");
        builder.AppendLine($"cp -R \"{content}/.\" \"{app}/\"");
        builder.AppendLine($"chmod +x \"{relaunch}\" 2>/dev/null || true");
        builder.AppendLine($"nohup \"{relaunch}\" >/dev/null 2>&1 &");
        builder.AppendLine($"rm -rf \"{workspace}\"");
        return builder.ToString();
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
            builder.Append(invalid.Contains(character) ? '_' : character);

        return builder.Length == 0 ? "update" : builder.ToString();
    }
}
