using System.Text.Json;

namespace PCL.Kernel.Versions;

public sealed record InstalledVersion(
    string Id,
    string Directory,
    string JsonPath,
    string? JarPath,
    string Type,
    string? InheritsFrom)
{
    /// <summary>实例自定义显示名称，来自实例覆盖设置。</summary>
    public string? Title { get; init; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Title) ? Id : $"{Title} ({Id})";
}

public static class InstalledVersionScanner
{
    public static IReadOnlyList<InstalledVersion> Scan(LauncherPaths paths)
    {
        if (!Directory.Exists(paths.VersionsDirectory))
            return [];

        var result = new List<InstalledVersion>();

        foreach (var directory in Directory.EnumerateDirectories(paths.VersionsDirectory))
        {
            var id = Path.GetFileName(directory);
            var jsonPath = Path.Combine(directory, id + ".json");
            if (!File.Exists(jsonPath))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString() ?? "release"
                    : "release";
                var inheritsFrom = root.TryGetProperty("inheritsFrom", out var inheritsElement)
                    ? inheritsElement.GetString()
                    : null;
                var jarPath = Path.Combine(directory, id + ".jar");

                result.Add(new InstalledVersion(
                    id,
                    directory,
                    jsonPath,
                    File.Exists(jarPath) ? jarPath : null,
                    type,
                    inheritsFrom));
            }
            catch (JsonException)
            {
                // 损坏的版本文件不阻塞其他版本。
            }
            catch (IOException)
            {
                // 文件可能正被其他进程占用。
            }
        }

        return result
            .OrderByDescending(v => v.Type == "release")
            .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
