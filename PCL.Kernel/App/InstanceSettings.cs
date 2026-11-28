using System.Text.Json;

namespace PCL.Kernel.App;

/// <summary>针对单个实例的覆盖设置；空值表示跟随全局设置。</summary>
public sealed class InstanceSettings
{
    public string Title { get; set; } = "";

    public string JavaPath { get; set; } = "";

    public int? MaxMemoryMb { get; set; }

    public string JvmArguments { get; set; } = "";

    public string GameArguments { get; set; } = "";

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public void Normalize()
    {
        Title = (Title ?? "").Trim();
        JavaPath = (JavaPath ?? "").Trim();
        JvmArguments = JvmArguments ?? "";
        GameArguments = GameArguments ?? "";

        if (MaxMemoryMb is { } memory)
            MaxMemoryMb = Math.Clamp(memory, 512, 65536);

        if (WindowWidth is { } width)
            WindowWidth = Math.Clamp(width, 0, 16384);

        if (WindowHeight is { } height)
            WindowHeight = Math.Clamp(height, 0, 16384);
    }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) &&
        string.IsNullOrWhiteSpace(JavaPath) &&
        MaxMemoryMb is null &&
        string.IsNullOrWhiteSpace(JvmArguments) &&
        string.IsNullOrWhiteSpace(GameArguments) &&
        WindowWidth is null &&
        WindowHeight is null;

    public InstanceSettings Clone()
    {
        Normalize();
        return new InstanceSettings
        {
            Title = Title,
            JavaPath = JavaPath,
            MaxMemoryMb = MaxMemoryMb,
            JvmArguments = JvmArguments,
            GameArguments = GameArguments,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight
        };
    }
}

/// <summary>按版本 ID 持久化实例覆盖设置，存储在启动器数据目录。</summary>
public sealed class InstanceSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, InstanceSettings> _items;

    private InstanceSettingsStore(string path, Dictionary<string, InstanceSettings> items)
    {
        _path = path;
        _items = items;
    }

    public static InstanceSettingsStore Load(LauncherPaths paths)
    {
        var path = Path.Combine(paths.DataDirectory, "instance.settings.json");

        try
        {
            if (!File.Exists(path))
                return new InstanceSettingsStore(
                    path, new Dictionary<string, InstanceSettings>(StringComparer.OrdinalIgnoreCase));

            var items = JsonSerializer.Deserialize<Dictionary<string, InstanceSettings>>(
                            File.ReadAllText(path), Options)
                        ?? new Dictionary<string, InstanceSettings>();

            foreach (var settings in items.Values)
                settings.Normalize();

            return new InstanceSettingsStore(
                path,
                new Dictionary<string, InstanceSettings>(
                    items, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new InstanceSettingsStore(
                path, new Dictionary<string, InstanceSettings>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public InstanceSettings Get(string versionId) =>
        _items.TryGetValue(versionId, out var settings)
            ? settings.Clone()
            : new InstanceSettings();

    public void Set(string versionId, InstanceSettings settings)
    {
        settings.Normalize();

        if (settings.IsEmpty)
        {
            if (_items.Remove(versionId))
                Save();

            return;
        }

        _items[versionId] = settings.Clone();
        Save();
    }

    public void Remove(string versionId)
    {
        if (_items.Remove(versionId))
            Save();
    }

    /// <summary>把源实例的覆盖设置复制到新实例 ID。</summary>
    public void Duplicate(string sourceId, string targetId)
    {
        if (string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
            return;

        var settings = Get(sourceId);
        if (!settings.IsEmpty)
            Set(targetId, settings);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_items, Options);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, true);
    }
}
