using System.IO.Compression;
using System.Text;

namespace PCL.Kernel.GameData;

public sealed record DiagnosticExportResult(
    string Path,
    int FilesBundled,
    int IssueCount);

/// <summary>
/// 将日志、崩溃报告、模组与版本清单打包成 zip 诊断报告，便于用户反馈问题。
/// 只读取游戏目录中的本地文件，不上传任何内容。
/// </summary>
public static class DiagnosticExporter
{
    public static DiagnosticExportResult Export(
        string gameDirectory,
        string destinationPath,
        string? launcherVersion = null)
    {
        var analysis = LogAnalyzer.Analyze(gameDirectory);
        var bundled = 0;

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                WriteText(archive, "diagnostics.txt", BuildSummary(
                    gameDirectory, launcherVersion, analysis));

                bundled += BundleFile(archive, Path.Combine(gameDirectory, "logs", "latest.log"),
                    "logs/latest.log");

                var crashDirectory = Path.Combine(gameDirectory, "crash-reports");
                if (Directory.Exists(crashDirectory))
                {
                    foreach (var report in Directory.EnumerateFiles(crashDirectory, "*.txt")
                                 .Select(path => new FileInfo(path))
                                 .OrderByDescending(info => info.LastWriteTimeUtc)
                                 .Take(5))
                        bundled += BundleFile(archive, report.FullName,
                            "crash-reports/" + report.Name);
                }

                bundled += WriteListing(archive, "mods.txt",
                    Path.Combine(gameDirectory, "mods"));
                bundled += WriteListing(archive, "versions.txt",
                    Path.Combine(gameDirectory, "versions"));
            }

            File.Move(tempPath, destinationPath, true);
            return new DiagnosticExportResult(
                Path.GetFullPath(destinationPath), bundled, analysis.Issues.Count);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string BuildSummary(
        string gameDirectory,
        string? launcherVersion,
        LogAnalysisResult analysis)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PCL Community Edition 诊断报告");
        builder.AppendLine("生成时间：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        builder.AppendLine("启动器版本：" + (launcherVersion ?? "unknown"));
        builder.AppendLine("游戏目录：" + Path.GetFullPath(gameDirectory));
        builder.AppendLine("系统：" + PlatformInfo.DisplayName + " · " +
                           PlatformInfo.Architecture + " · " + PlatformInfo.OsVersion);
        builder.AppendLine("扫描日志文件：" + analysis.FilesScanned);
        builder.AppendLine();

        if (analysis.Issues.Count == 0)
        {
            builder.AppendLine("未匹配到已知问题。");
            return builder.ToString();
        }

        builder.AppendLine("匹配到的问题：");
        foreach (var issue in analysis.Issues)
        {
            builder.AppendLine($"[{issue.Source}] {issue.Title}");
            builder.AppendLine("  建议：" + issue.Hint);
            builder.AppendLine("  证据：" + issue.Detail);
        }

        return builder.ToString();
    }

    private static int BundleFile(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
            return 0;

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var target = entry.Open();
        using var source = File.OpenRead(sourcePath);
        source.CopyTo(target);
        return 1;
    }

    private static int WriteListing(ZipArchive archive, string entryName, string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        var names = Directory.EnumerateFileSystemEntries(directory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        WriteText(archive, entryName, string.Join('\n', names));
        return names.Length;
    }

    private static void WriteText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
