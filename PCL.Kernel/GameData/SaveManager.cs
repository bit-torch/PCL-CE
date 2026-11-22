using System.IO.Compression;

namespace PCL.Kernel.GameData;

/// <summary>存档导出、导入与删除。导出为包含单个世界目录的 zip。</summary>
public static class SaveManager
{
    public static string Export(string gameDirectory, string saveName, string destinationPath)
    {
        var source = ResolveSaveDirectory(gameDirectory, saveName);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"存档不存在：{saveName}");

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .ToArray();

        try
        {
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                var root = Path.GetFileName(source.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                foreach (var file in files)
                {
                    var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    var entry = archive.CreateEntry($"{root}/{relative}", CompressionLevel.Optimal);
                    using var target = entry.Open();
                    using var input = File.OpenRead(file);
                    input.CopyTo(target);
                }
            }

            File.Move(tempPath, destinationPath, true);
            return Path.GetFullPath(destinationPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>导入存档 zip，返回新存档目录。同名时自动追加序号。</summary>
    public static string Import(string gameDirectory, string archivePath)
    {
        var savesRoot = Path.GetFullPath(Path.Combine(gameDirectory, "saves"));
        Directory.CreateDirectory(savesRoot);

        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToArray();

        if (entries.Length == 0)
            throw new InvalidDataException("存档压缩包为空。");

        var roots = entries
            .Select(entry => entry.FullName.Split('/')[0])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var hasSingleRoot = roots.Length == 1 &&
                            entries.All(entry => entry.FullName.Contains('/'));
        var name = hasSingleRoot
            ? roots[0]
            : Path.GetFileNameWithoutExtension(archivePath);

        if (string.IsNullOrWhiteSpace(name))
            name = "Imported";

        var target = UniqueDirectory(savesRoot, name);
        Directory.CreateDirectory(target);

        try
        {
            foreach (var entry in entries)
            {
                var relative = hasSingleRoot
                    ? entry.FullName[(entry.FullName.IndexOf('/') + 1)..]
                    : entry.FullName;

                var destination = SafeCombine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }

            return target;
        }
        catch
        {
            try
            {
                Directory.Delete(target, true);
            }
            catch (IOException)
            {
                // 忽略清理失败，保留原始异常。
            }

            throw;
        }
    }

    public static void Delete(string gameDirectory, string saveName)
    {
        var directory = ResolveSaveDirectory(gameDirectory, saveName);
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static string UniqueDirectory(string root, string name)
    {
        var target = Path.Combine(root, Sanitize(name));
        if (!Directory.Exists(target))
            return target;

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(root, $"{Sanitize(name)} ({index})");
            if (!Directory.Exists(candidate))
                return candidate;
        }
    }

    private static string ResolveSaveDirectory(string gameDirectory, string saveName)
    {
        var savesRoot = Path.GetFullPath(Path.Combine(gameDirectory, "saves"));
        var path = Path.GetFullPath(Path.Combine(savesRoot, saveName));
        var relative = Path.GetRelativePath(savesRoot, path);

        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) ||
            relative.Contains(Path.DirectorySeparatorChar))
            throw new InvalidDataException($"非法的存档名称：{saveName}");

        return path;
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
            throw new InvalidDataException($"存档包含非法路径：{relativePath}");

        return fullPath;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);

        foreach (var character in name)
            builder.Append(invalid.Contains(character) ? '_' : character);

        return builder.Length == 0 ? "Imported" : builder.ToString();
    }
}
