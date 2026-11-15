using System.Text.Json;
using PCL.Core.App;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Models;
using PCL.Core.Utils.Secret;

namespace PCL.Avalonia;

public sealed class DesktopSettings
{
    public string GameDirectory { get; set; } = DefaultGameDirectory();
    public string JavaPath { get; set; } = OperatingSystem.IsWindows() ? "java.exe" : "java";
    public int MemoryMb { get; set; } = 2048;
    public string OfflineName { get; set; } = "Player";
    public string? SelectedVersion { get; set; }
    public string MicrosoftClientId { get; set; } = "";
    public string? EncryptedAccount { get; set; }
    public bool DarkTheme { get; set; }

    public static string FilePath => Path.Combine(Paths.SharedData, "avalonia.settings.json");

    public static DesktopSettings Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("设置文件为空。")
        : new();

    public void Save()
    {
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }

    public McProfile? LoadAccount() => string.IsNullOrEmpty(EncryptedAccount) ? null
        : JsonSerializer.Deserialize<McProfile>(EncryptHelper.SecretDecrypt(EncryptedAccount));

    public void StoreAccount(AuthenticationResult result)
    {
        var profile = new McProfile
        {
            ProfileId = Guid.NewGuid().ToString("N"), ProfileType = result.ProfileType,
            UserName = result.UserName, Uuid = result.Uuid, AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken, ExpiresAt = result.ExpiresAt, ClientToken = result.ClientToken,
            Provider = result.Provider, Server = result.Server, LoginName = result.LoginName
        };
        EncryptedAccount = EncryptHelper.SecretEncrypt(JsonSerializer.Serialize(profile));
    }

    private static string DefaultGameDirectory()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : OperatingSystem.IsMacOS() ? Path.Combine(user, "Library", "Application Support", "minecraft")
            : Path.Combine(user, ".minecraft");
    }
}
