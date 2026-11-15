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
    public Dictionary<string, InstancePreferences> Instances { get; set; } = new();
    public string? EncryptedAccounts { get; set; }
    public string? SelectedAccountId { get; set; }
    public Dictionary<string, string> ProviderClientIds { get; set; } = new();
    public PCL.Core.IO.Net.Http.HttpProxyManager.ProxyMode ProxyMode { get; set; } = PCL.Core.IO.Net.Http.HttpProxyManager.ProxyMode.SystemProxy;
    public string ProxyAddress { get; set; } = "";
    public string DownloadMirror { get; set; } = "";
    public string? EncryptedCurseForgeKey { get; set; }
    public List<FavoriteResource> Favorites { get; set; } = [];
    public string Language { get; set; } = "zh-CN";
    public string? BackgroundImage { get; set; }
    public string? HomepageFile { get; set; }
    public string? VlcLibraryDirectory { get; set; }
    public int MusicVolume { get; set; } = 60;
    public bool ShuffleMusic { get; set; }
    public bool LoopMusic { get; set; } = true;
    public string? MusicFile { get; set; }
    public string? EasyTierDirectory { get; set; }
    public LaunchWindowBehavior WindowBehavior { get; set; }
    public bool CheckUpdatesOnStartup { get; set; }
    public bool AutoAnalyzeCrash { get; set; } = true;
    public string AccentColor { get; set; } = "#0078D4";
    public string? FontName { get; set; }
    public double BackgroundOpacity { get; set; } = 0.18;
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

    public List<McProfile> Accounts() => string.IsNullOrEmpty(EncryptedAccounts)
        ? (LoadAccount() is { } legacy ? [legacy] : [])
        : JsonSerializer.Deserialize<List<McProfile>>(EncryptHelper.SecretDecrypt(EncryptedAccounts)) ?? [];

    public McProfile? SelectedAccount() { var accounts = Accounts(); return accounts.FirstOrDefault(p => p.ProfileId == SelectedAccountId) ?? accounts.FirstOrDefault(); }
    public void RemoveSelectedAccount()
    {
        var selected = SelectedAccount(); var accounts = Accounts(); accounts.RemoveAll(p => p.ProfileId == selected?.ProfileId);
        EncryptedAccounts = EncryptHelper.SecretEncrypt(JsonSerializer.Serialize(accounts)); EncryptedAccount = null;
        SelectedAccountId = accounts.FirstOrDefault()?.ProfileId;
    }
    public void StoreAccount(AuthenticationResult result)
    {
        var accounts = Accounts();
        var existing = accounts.FirstOrDefault(p => p.Uuid == result.Uuid && p.ProfileType == result.ProfileType && p.Server == result.Server);
        var profile = new McProfile
        {
            ProfileId = existing?.ProfileId ?? Guid.NewGuid().ToString("N"), ProfileType = result.ProfileType,
            UserName = result.UserName, Uuid = result.Uuid, AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken, ExpiresAt = result.ExpiresAt, ClientToken = result.ClientToken,
            Provider = result.Provider, Server = result.Server, LoginName = result.LoginName,
            DiscoveryAddress = result.DiscoveryAddress, IdToken = result.IdToken
        };
        accounts.RemoveAll(p => p.ProfileId == profile.ProfileId); accounts.Add(profile);
        EncryptedAccounts = EncryptHelper.SecretEncrypt(JsonSerializer.Serialize(accounts));
        EncryptedAccount = null; SelectedAccountId = profile.ProfileId;
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

public sealed class InstancePreferences
{
    public string? JavaPath { get; set; }
    public int MemoryMb { get; set; }
    public bool Isolated { get; set; }
    public string JvmArguments { get; set; } = "";
    public string GameArguments { get; set; } = "";
    public string EnvironmentVariables { get; set; } = "";
    public int Width { get; set; } = 854;
    public int Height { get; set; } = 480;
    public string ServerAddress { get; set; } = "";
}
public sealed record FavoriteResource(PCL.CrossPlatform.ResourceProvider Provider, string Id, string Title, string Url);

public enum LaunchWindowBehavior { Keep, Minimize, Hide, Exit }
