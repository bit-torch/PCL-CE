using System.Text.Json;

namespace PCL.Kernel.App;

public sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = "";

    public string? JavaPath { get; set; }

    public int MaxMemoryMb { get; set; } = 4096;

    public string OfflineUsername { get; set; } = "Player";

    public List<string> OfflineAccounts { get; set; } = [];

    public bool IsolateVersions { get; set; }

    public bool DarkTheme { get; set; }

    public bool LicensePrecheckAccepted { get; set; }

    public int LaunchCount { get; set; }

    /// <summary>补齐旧配置缺少的账户列表，并保证当前用户名为有效账户。</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(OfflineUsername))
            OfflineUsername = "Player";

        OfflineAccounts ??= [];

        if (!OfflineAccounts.Contains(OfflineUsername, StringComparer.OrdinalIgnoreCase))
            OfflineAccounts.Add(OfflineUsername);
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return CreateDefault();

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json, Options)
                           ?? CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CreateDefault();
        }
    }

    private static LauncherSettings CreateDefault()
    {
        var settings = new LauncherSettings();
        settings.Normalize();
        return settings;
    }

    public static void Save(string path, LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, Options);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }
}
