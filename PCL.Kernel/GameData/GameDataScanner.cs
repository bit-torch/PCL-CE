namespace PCL.Kernel.GameData;

/// <summary>扫描 saves 目录，读取存档名称、图标与最后修改时间。</summary>
public static class SaveScanner
{
    public static IReadOnlyList<SaveEntry> Scan(string gameDirectory)
    {
        var root = Path.Combine(gameDirectory, "saves");
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Select(directory =>
            {
                var icon = Path.Combine(directory, "icon.png");
                return new SaveEntry(
                    Path.GetFileName(directory),
                    directory,
                    File.Exists(icon) ? icon : null,
                    new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero));
            })
            .OrderByDescending(entry => entry.LastModified)
            .ToArray();
    }
}

/// <summary>扫描 screenshots 目录中的 PNG / JPEG 截图。</summary>
public static class ScreenshotScanner
{
    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

    public static IReadOnlyList<ScreenshotEntry> Scan(string gameDirectory)
    {
        var root = Path.Combine(gameDirectory, "screenshots");
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateFiles(root)
            .Where(path => Extensions.Contains(Path.GetExtension(path)))
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new ScreenshotEntry(
                    info.Name,
                    info.FullName,
                    new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                    info.Length);
            })
            .OrderByDescending(entry => entry.Created)
            .ToArray();
    }
}
