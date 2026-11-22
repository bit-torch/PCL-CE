using System.Text.Json.Serialization;
using PCL.Kernel.Launch;

namespace PCL.Kernel.Accounts;

public enum AccountKind
{
    Offline,
    Microsoft,
    Yggdrasil
}

/// <summary>启动器账户。离线、微软正版与 Yggdrasil 第三方账户共用同一结构。</summary>
public sealed class LauncherAccount
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public AccountKind Kind { get; set; }

    public string AccessToken { get; set; } = "";

    public string? RefreshToken { get; set; }

    /// <summary>Yggdrasil 登录返回的 clientToken。</summary>
    public string? ClientToken { get; set; }

    /// <summary>Yggdrasil API Root，例如 https://littleskin.cn/api/yggdrasil。</summary>
    public string? ApiRoot { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore]
    public bool IsOnline => Kind is not AccountKind.Offline;

    [JsonIgnore]
    public string KindName => Kind switch
    {
        AccountKind.Microsoft => "Microsoft",
        AccountKind.Yggdrasil => "Yggdrasil",
        _ => "Offline"
    };

    [JsonIgnore]
    public string Display => $"{Name} · {KindName}";

    public static LauncherAccount Offline(string name) => new()
    {
        Id = OfflineAccount.CreateUuid(name),
        Name = name,
        Kind = AccountKind.Offline
    };

    public override string ToString() => Display;
}

public sealed record DeviceCodeInfo(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int ExpiresIn,
    int Interval);

public sealed record MicrosoftToken(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public sealed record MinecraftProfile(
    string Id,
    string Name,
    string AccessToken,
    DateTimeOffset ExpiresAt);

public static class YggdrasilServer
{
    /// <summary>接受 API Root 或完整的 authserver 地址，统一为 API Root。</summary>
    public static string NormalizeApiRoot(string apiRoot)
    {
        var value = apiRoot.Trim().TrimEnd('/');
        const string suffix = "/authserver";
        if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            value = value[..^suffix.Length].TrimEnd('/');
        return value;
    }
}
