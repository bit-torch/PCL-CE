namespace PCL.Kernel.GameData;

/// <summary>mods 目录中的一个模组文件（含已禁用文件）。</summary>
public sealed record ModEntry(
    string FileName,
    string FilePath,
    bool Enabled,
    long Size,
    DateTimeOffset Modified)
{
    public string DisplayName => FileName.EndsWith(
        ModScanner.DisabledSuffix, StringComparison.OrdinalIgnoreCase)
        ? FileName[..^ModScanner.DisabledSuffix.Length]
        : FileName;

    public string SizeLabel => Size >= 1024 * 1024
        ? $"{Size / (1024d * 1024d):0.#} MB"
        : $"{Size / 1024d:0.#} KB";
}

/// <summary>扫描并启停 mods 目录中的模组。禁用通过重命名为 .jar.disabled 实现。</summary>
public static class ModScanner
{
    internal const string DisabledSuffix = ".disabled";

    public static IReadOnlyList<ModEntry> Scan(string gameDirectory)
    {
        var directory = Path.Combine(gameDirectory, "mods");
        if (!Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory)
            .Where(IsModFile)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var enabled = !path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                return new ModEntry(
                    info.Name,
                    info.FullName,
                    enabled,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            })
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>切换模组启用状态，返回切换后的文件名。</summary>
    public static string SetEnabled(string gameDirectory, string fileName, bool enabled)
    {
        var path = Resolve(gameDirectory, fileName);
        var isDisabled = path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

        if (enabled)
        {
            if (isDisabled)
            {
                var target = path[..^DisabledSuffix.Length];
                if (File.Exists(target))
                    throw new IOException($"已存在同名模组：{Path.GetFileName(target)}");

                File.Move(path, target);
                return Path.GetFileName(target);
            }

            return Path.GetFileName(path);
        }

        if (!isDisabled)
        {
            var target = path + DisabledSuffix;
            if (File.Exists(target))
                throw new IOException($"已存在同名禁用文件：{Path.GetFileName(target)}");

            File.Move(path, target);
            return Path.GetFileName(target);
        }

        return Path.GetFileName(path);
    }

    public static void Delete(string gameDirectory, string fileName) =>
        File.Delete(Resolve(gameDirectory, fileName));

    private static bool IsModFile(string path) =>
        path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

    private static string Resolve(string gameDirectory, string fileName)
    {
        var directory = Path.GetFullPath(Path.Combine(gameDirectory, "mods"));
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        var relative = Path.GetRelativePath(directory, path);

        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException($"非法的模组文件名：{fileName}");

        return path;
    }
}
