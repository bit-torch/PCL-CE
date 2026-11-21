namespace PCL.Kernel.GameData;

public sealed record LogFileEntry(
    string Name,
    string Path,
    DateTime LastModified,
    long Size)
{
    public string SizeLabel => Size >= 1024 * 1024
        ? $"{Size / 1024d / 1024d:0.0} MB"
        : $"{Math.Max(1, Size / 1024)} KB";

    public string LastModifiedLabel => LastModified.ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>扫描游戏目录及其隔离目录下的日志文件。</summary>
public static class LogScanner
{
    public static IReadOnlyList<LogFileEntry> Scan(IEnumerable<string> gameDirectories)
    {
        var entries = new List<LogFileEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gameDirectory in gameDirectories
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var logsDirectory = System.IO.Path.Combine(gameDirectory, "logs");
            if (!Directory.Exists(logsDirectory))
                continue;

            foreach (var path in Directory.EnumerateFiles(logsDirectory, "*.log"))
            {
                if (!seen.Add(path))
                    continue;

                var info = new FileInfo(path);
                entries.Add(new LogFileEntry(
                    info.Name, info.FullName, info.LastWriteTime, info.Length));
            }
        }

        return entries
            .OrderByDescending(entry => entry.LastModified)
            .ToList();
    }
}

/// <summary>读取日志尾部或按关键词筛选，避免一次性渲染超大日志。</summary>
public static class LogReader
{
    private const int MaxLines = 20000;

    public static async Task<string> ReadTailAsync(
        string path,
        int maxLines = 500,
        CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        return string.Join(
            Environment.NewLine,
            lines.TakeLast(Math.Clamp(maxLines, 1, MaxLines)));
    }

    public static async Task<string> SearchAsync(
        string path,
        string keyword,
        int maxLines = 500,
        CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken)
            .ConfigureAwait(false);

        var filtered = string.IsNullOrWhiteSpace(keyword)
            ? lines
            : lines.Where(line =>
                    line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return string.Join(
            Environment.NewLine,
            filtered.TakeLast(Math.Clamp(maxLines, 1, MaxLines)));
    }
}
