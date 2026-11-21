using System.Text.Json;

namespace PCL.Kernel.GameData;

/// <summary>resourcepacks 目录中的一个资源包（zip 文件或文件夹）。</summary>
public sealed record ResourcePackEntry(
    string Name,
    string Path,
    bool IsDirectory,
    bool Enabled,
    long Size)
{
    public string DisplayName => IsDirectory || !Name.EndsWith(
        ".zip", StringComparison.OrdinalIgnoreCase)
        ? Name
        : Name[..^4];

    public string SizeLabel => IsDirectory
        ? ""
        : Size >= 1024 * 1024
            ? $"{Size / (1024d * 1024d):0.#} MB"
            : $"{Size / 1024d:0.#} KB";
}

/// <summary>
/// 通过 options.txt 的 resourcePacks 列表启停与排序资源包。
/// 列表顺序即游戏内的优先级顺序：越靠后优先级越高，vanilla 始终位于最前。
/// </summary>
public static class ResourcePackManager
{
    private const string OptionsFileName = "options.txt";
    private const string ResourcePacksKey = "resourcePacks";
    private const string FilePrefix = "file/";

    public static IReadOnlyList<ResourcePackEntry> Scan(string gameDirectory)
    {
        var directory = Path.Combine(gameDirectory, "resourcepacks");
        if (!Directory.Exists(directory))
            return [];

        var selection = ReadSelection(gameDirectory);
        var entries = new List<(ResourcePackEntry Entry, int Order)>();

        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(path);
            var isDirectory = Directory.Exists(path);

            if (!isDirectory && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            var order = selection.FindIndex(id => IsPackId(id, name));
            var size = isDirectory ? 0 : new FileInfo(path).Length;
            entries.Add((new ResourcePackEntry(
                name,
                Path.GetFullPath(path),
                isDirectory,
                order >= 0,
                size), order));
        }

        return entries
            .OrderBy(item => item.Order < 0 ? int.MaxValue : item.Order)
            .ThenBy(item => item.Entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Entry)
            .ToArray();
    }

    /// <summary>启用或禁用资源包；启用时加入列表末尾（最高优先级）。</summary>
    public static void SetEnabled(string gameDirectory, string name, bool enabled)
    {
        name = ValidateName(name);
        var selection = ReadSelection(gameDirectory);
        selection.RemoveAll(id => IsPackId(id, name));

        if (enabled)
            selection.Add(FilePrefix + name);

        WriteSelection(gameDirectory, selection);
    }

    /// <summary>
    /// 在已启用的资源包之间上移或下移一位。offset 为 -1 表示降低优先级，1 表示提高优先级。
    /// </summary>
    public static bool Move(string gameDirectory, string name, int offset)
    {
        name = ValidateName(name);
        if (offset == 0)
            return false;

        var selection = ReadSelection(gameDirectory);
        var index = selection.FindIndex(id => IsPackId(id, name));
        if (index < 0)
            return false;

        var direction = offset > 0 ? 1 : -1;
        var neighbor = FindNeighbor(selection, index, direction);
        if (neighbor < 0)
            return false;

        (selection[index], selection[neighbor]) = (selection[neighbor], selection[index]);
        WriteSelection(gameDirectory, selection);
        return true;
    }

    private static int FindNeighbor(List<string> selection, int index, int direction)
    {
        for (var i = index + direction; i >= 0 && i < selection.Count; i += direction)
        {
            if (selection[i].StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static bool IsPackId(string id, string name)
    {
        var value = id.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
            ? id[FilePrefix.Length..]
            : id;

        const string folderPrefix = "resourcepacks/";
        if (value.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            value = value[folderPrefix.Length..];

        return string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            Path.IsPathRooted(name) ||
            name.Contains('/') ||
            name.Contains('\\') ||
            name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"非法的资源包名称：{name}");

        return name;
    }

    private static List<string> ReadSelection(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, OptionsFileName);
        if (!File.Exists(path))
            return [];

        foreach (var line in File.ReadAllLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            if (!string.Equals(
                    line[..separator].Trim(), ResourcePacksKey, StringComparison.Ordinal))
                continue;

            return ParseArray(line[(separator + 1)..]);
        }

        return [];
    }

    private static List<string> ParseArray(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return value.Trim('[', ']')
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim().Trim('"', '\''))
                .Where(item => item.Length > 0)
                .ToList();
        }
    }

    private static void WriteSelection(string gameDirectory, IReadOnlyList<string> selection)
    {
        Directory.CreateDirectory(gameDirectory);
        var path = Path.Combine(gameDirectory, OptionsFileName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var value = JsonSerializer.Serialize(selection);

        var replaced = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var separator = lines[i].IndexOf(':');
            if (separator <= 0 ||
                !string.Equals(lines[i][..separator].Trim(),
                    ResourcePacksKey, StringComparison.Ordinal))
                continue;

            if (!replaced)
            {
                lines[i] = $"{ResourcePacksKey}:{value}";
                replaced = true;
            }
            else
            {
                lines.RemoveAt(i);
                i--;
            }
        }

        if (!replaced)
            lines.Add($"{ResourcePacksKey}:{value}");

        var temp = path + ".tmp";
        File.WriteAllLines(temp, lines);
        File.Move(temp, path, true);
    }
}
