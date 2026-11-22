using System.IO.Compression;

namespace PCL.Kernel.Versions;

public sealed record InstanceExportResult(
    string ArchivePath,
    int FileCount,
    long TotalBytes);

/// <summary>
/// 将实例（版本目录及可选的隔离游戏目录）打包为 zip，便于备份与迁移。
/// </summary>
public static class InstanceExporter
{
    public static async Task<InstanceExportResult> ExportAsync(
        InstalledVersion version,
        string destinationZip,
        string? gameDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var versionDirectory = Path.GetFullPath(version.Directory);
        if (!Directory.Exists(versionDirectory))
            throw new DirectoryNotFoundException($"版本目录不存在：{versionDirectory}");

        var destination = Path.GetFullPath(destinationZip);
        if (!destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            destination += ".zip";

        if (destination.StartsWith(
                versionDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("导出文件不能放在版本目录内。");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
            File.Delete(destination);

        var fileCount = 0;
        long totalBytes = 0;

        await using var file = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            var prefix = string.IsNullOrWhiteSpace(version.Id)
                ? Path.GetFileName(versionDirectory)
                : version.Id;

            AddDirectory(archive, versionDirectory, prefix);

            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                var gameRoot = Path.GetFullPath(gameDirectory);
                if (Directory.Exists(gameRoot) &&
                    !gameRoot.StartsWith(
                        versionDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    AddDirectory(archive, gameRoot, prefix + "/game");
            }

            void AddDirectory(ZipArchive target, string source, string entryPrefix)
            {
                foreach (var path in Directory.EnumerateFiles(
                             source, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relative = Path.GetRelativePath(source, path)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    target.CreateEntryFromFile(
                        path, entryPrefix + "/" + relative, CompressionLevel.Optimal);
                    fileCount++;
                    totalBytes += new FileInfo(path).Length;
                }
            }
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new InstanceExportResult(destination, fileCount, totalBytes);
    }
}
