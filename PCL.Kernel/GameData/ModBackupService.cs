using System.IO.Compression;

namespace PCL.Kernel.GameData;

/// <summary>mods 目录下的一次模组备份。</summary>
public sealed record ModBackup(
    string FileName,
    string DirectoryPath,
    string FilePath,
    long Size,
    DateTimeOffset Created)
{
    public string CreatedLabel => Created.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string SizeLabel => Size >= 1024 * 1024
        ? $"{Size / (1024d * 1024d):0.#} MB"
        : $"{Size / 1024d:0.#} KB";

    public ModBackup WithCreated(DateTimeOffset created) => this with { Created = created };
}

/// <summary>
/// 把模组复制到 mods/.pcl-backups/&lt;时间戳&gt;/ 下，恢复时覆盖同名文件。
/// 备份目录不参与模组扫描，也不会被误启用。
/// </summary>
public static class ModBackupService
{
    private const string FolderName = ".pcl-backups";

    public static string Create(string gameDirectory, string fileName)
    {
        var modsDirectory = Path.Combine(gameDirectory, "mods");
        var source = Path.Combine(modsDirectory, fileName);
        if (!File.Exists(source))
            throw new FileNotFoundException("找不到要备份的模组。", source);

        var directory = Path.Combine(
            modsDirectory,
            FolderName,
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, Path.GetFileName(fileName));
        File.Copy(source, destination, false);
        return destination;
    }

    public static IReadOnlyList<ModBackup> Scan(string gameDirectory)
    {
        var root = Path.Combine(gameDirectory, "mods", FolderName);
        if (!Directory.Exists(root))
            return [];

        var result = new List<ModBackup>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var created = new DateTimeOffset(
                Directory.GetCreationTimeUtc(directory), TimeSpan.Zero);

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var info = new FileInfo(file);
                result.Add(new ModBackup(
                    info.Name,
                    directory,
                    info.FullName,
                    info.Length,
                    created));
            }
        }

        return result
            .OrderByDescending(backup => backup.Created)
            .ToArray();
    }

    /// <summary>把备份复制回 mods 目录，同名文件会被覆盖。</summary>
    public static string Restore(string gameDirectory, ModBackup backup)
    {
        var modsDirectory = Path.Combine(gameDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);

        var target = Path.Combine(modsDirectory, backup.FileName);
        File.Copy(backup.FilePath, target, true);
        return target;
    }

    public static void Delete(ModBackup backup)
    {
        if (Directory.Exists(backup.DirectoryPath))
            Directory.Delete(backup.DirectoryPath, true);
    }
}

/// <summary>一个 mod jar 内部打包的嵌套模组（Jar-in-Jar）。</summary>
public sealed record NestedMod(string EntryName, long Size)
{
    public string SizeLabel => Size >= 1024 * 1024
        ? $"{Size / (1024d * 1024d):0.#} MB"
        : $"{Size / 1024d:0.#} KB";
}

/// <summary>读取 mod jar 中通过 jar-in-jar 打包的嵌套模组，只列出路径与大小。</summary>
public static class JarInJarScanner
{
    private const string MetadataEntry = "META-INF/jarjar/metadata.json";

    public static IReadOnlyList<NestedMod> Scan(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var paths = ReadMetadataPaths(archive);

        if (paths.Count == 0)
        {
            paths.AddRange(archive.Entries
                .Where(entry => entry.FullName.EndsWith(
                    ".jar", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.FullName));
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new { Path = path, Entry = archive.GetEntry(path) })
            .Where(item => item.Entry is not null)
            .Select(item => new NestedMod(item.Path, item.Entry!.Length))
            .OrderBy(item => item.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static List<string> ReadMetadataPaths(ZipArchive archive)
    {
        var result = new List<string>();
        var entry = archive.GetEntry(MetadataEntry);
        if (entry is null)
            return result;

        try
        {
            using var stream = entry.Open();
            using var document = System.Text.Json.JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("jars", out var jars) ||
                jars.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return result;
            }

            foreach (var jar in jars.EnumerateArray())
            {
                if (jar.TryGetProperty("path", out var path) &&
                    path.GetString() is { Length: > 0 } value)
                {
                    result.Add(value);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            // 元数据损坏时回退到扫描所有内嵌 jar。
        }

        return result;
    }
}
