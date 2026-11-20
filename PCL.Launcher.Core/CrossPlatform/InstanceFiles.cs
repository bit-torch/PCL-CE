using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PCL.CrossPlatform;

public sealed record InstanceFile(string Path, string Name, long Size, bool Enabled)
{
    public override string ToString() => $"{(Enabled ? "✓" : "○")} {Name} · {Size / 1024:N0} KiB";
}

/// <summary>Local instance operations. Replacements and removals retain recoverable copies.</summary>
public static class InstanceFiles
{
    public static string DataDirectory(string root, string id, bool isolated)
    {
        GameManifest.ValidateId(id);
        return isolated ? GameManifest.SafePath(root, "instances/" + id) : Path.GetFullPath(root);
    }

    public static IReadOnlyList<InstanceFile> List(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory).Select(path => new FileInfo(path))
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new InstanceFile(file.FullName, file.Name, file.Length, !file.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public static string Toggle(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("文件不存在。", file);
        var target = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? file[..^9] : file + ".disabled";
        File.Move(file, target, false);
        return target;
    }

    public static string Archive(string path, string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);
        var destination = Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + "-" + Path.GetFileName(path));
        if (Directory.Exists(path)) Directory.Move(path, destination);
        else File.Move(path, destination);
        return destination;
    }

    public static async Task ImportFileAsync(string source, string directory, CancellationToken token = default)
    {
        Directory.CreateDirectory(directory);
        var destination = GameManifest.SafePath(directory, Path.GetFileName(source));
        if (File.Exists(destination)) throw new IOException("同名文件已存在，请先移除旧文件或更改文件名。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, token);
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task ExtractAsync(string archivePath, string destination, string prefix = "", CancellationToken token = default)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            // Validate even excluded entries. ZIP Unix symlinks are never materialized.
            GameManifest.SafePath(destination, entry.FullName.TrimEnd('/'));
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("压缩包包含符号链接。");
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/')) continue;
            var relative = entry.FullName[prefix.Length..];
            if (relative.Length == 0) continue;
            var target = GameManifest.SafePath(destination, relative);
            EnsureNoLinks(destination, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
        }
    }

    public static async Task ExportAsync(string source, string destination, CancellationToken token = default)
    {
        var files = EnumerateSafeFiles(source).ToArray();
        if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(source) + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidOperationException("导出目标不能位于被导出的目录中。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
                // Launcher credentials, keys and runtime locks are never exported with a game instance.
                if (relative.Split('/').Any(p => p is "PCL" or ".pcl-trash") || relative.EndsWith(".lock") ||
                    Path.GetFileName(relative) is "launcher_accounts.json" or "launcher_profiles.json" or "accounts.json" or "avalonia.settings.json" ||
                    Path.GetFileName(relative).StartsWith("UserKey", StringComparison.OrdinalIgnoreCase)) continue;
                var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                await using var input = File.OpenRead(file);
                await using var output = entry.Open();
                await input.CopyToAsync(output, token);
            }
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static IEnumerable<string> EnumerateSafeFiles(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("不支持导出链接目录。");
        foreach (var item in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(item);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("实例目录包含符号链接，操作已停止。");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in EnumerateSafeFiles(item)) yield return file;
            }
            else yield return item;
        }
    }

    internal static void EnsureNoLinks(string root, string destination)
    {
        var current = Path.GetFullPath(root);
        foreach (var component in Path.GetRelativePath(root, destination).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("目标路径包含符号链接。");
        }
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
