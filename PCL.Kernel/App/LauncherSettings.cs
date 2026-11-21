using System.Text.Json;

namespace PCL.Kernel.App;

public sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = "";

    public string? JavaPath { get; set; }

    public int MaxMemoryMb { get; set; } = 4096;

    public string OfflineUsername { get; set; } = "Player";

    public bool DarkTheme { get; set; }

    public bool LicensePrecheckAccepted { get; set; }

    public int LaunchCount { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new LauncherSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LauncherSettings>(json, Options)
                   ?? new LauncherSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
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
