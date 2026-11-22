using System.Net;
using System.Text.Json.Nodes;

namespace PCL.Kernel.Accounts;

/// <summary>
/// 微软正版认证：OAuth 设备码 → Xbox Live → XSTS → Minecraft Services → 角色档案。
/// 客户端 ID 由调用方提供，不使用任何上游仓库的私有凭据。
/// </summary>
public sealed class MicrosoftAuthenticator
{
    public const string DeviceCodeEndpoint =
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";

    public const string TokenEndpoint =
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    private const string Scope = "XboxLive.signin offline_access";

    public async Task<DeviceCodeInfo> RequestDeviceCodeAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("未配置 Microsoft 客户端 ID。");

        var response = await JsonHttp.PostFormAsync(DeviceCodeEndpoint,
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = Scope
            }, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess || response.Body is not JsonObject body)
            throw new InvalidOperationException($"获取设备码失败：{response.ErrorMessage}");

        return new DeviceCodeInfo(
            body["device_code"]?.ToString() ?? throw new InvalidDataException("设备码响应缺少 device_code。"),
            body["user_code"]?.ToString() ?? "",
            body["verification_uri"]?.ToString() ?? "https://microsoft.com/devicelogin",
            body["verification_uri_complete"]?.ToString(),
            body["expires_in"]?.GetValue<int>() ?? 900,
            Math.Max(1, body["interval"]?.GetValue<int>() ?? 5));
    }

    /// <summary>轮询设备码授权结果，直到用户完成登录或流程过期。</summary>
    public async Task<MicrosoftToken> WaitForTokenAsync(
        string clientId,
        DeviceCodeInfo info,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var interval = Math.Max(1, info.Interval);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(info.ExpiresIn);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);

            var response = await JsonHttp.PostFormAsync(TokenEndpoint,
                new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = clientId,
                    ["device_code"] = info.DeviceCode
                }, cancellationToken).ConfigureAwait(false);

            if (response.Body is not JsonObject body)
                throw new InvalidOperationException($"登录响应无法解析：{response.ErrorMessage}");

            if (body["access_token"] is not null)
                return ReadToken(body);

            var error = body["error"]?.ToString();
            switch (error)
            {
                case "authorization_pending":
                    status?.Report("等待浏览器端完成登录…");
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "expired_token":
                    throw new TimeoutException("设备码已过期，请重新开始登录。");
                case "access_denied":
                case "authorization_declined":
                    throw new InvalidOperationException("用户取消了 Microsoft 登录。");
                default:
                    throw new InvalidOperationException($"Microsoft 登录失败：{response.ErrorMessage}");
            }
        }

        throw new TimeoutException("等待 Microsoft 登录超时，请重新开始。");
    }

    public async Task<MicrosoftToken> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var response = await JsonHttp.PostFormAsync(TokenEndpoint,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["refresh_token"] = refreshToken,
                ["scope"] = Scope
            }, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess || response.Body is not JsonObject body)
            throw new InvalidOperationException($"刷新 Microsoft 令牌失败：{response.ErrorMessage}");

        return ReadToken(body);
    }

    /// <summary>用微软访问令牌换取 Minecraft 档案。</summary>
    public async Task<MinecraftProfile> AuthenticateMinecraftAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken = default)
    {
        var xbox = await JsonHttp.PostJsonAsync(
            "https://user.auth.xboxlive.com/user/authenticate",
            new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["AuthMethod"] = "RPS",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RpsTicket"] = "d=" + microsoftAccessToken
                },
                ["RelyingParty"] = "http://auth.xboxlive.com",
                ["TokenType"] = "JWT"
            }.ToJsonString(), cancellationToken).ConfigureAwait(false);

        var xboxToken = ReadXboxToken(xbox, "Xbox Live");
        var userHash = ReadUserHash(xbox);

        var xsts = await JsonHttp.PostJsonAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["SandboxId"] = "RETAIL",
                    ["UserTokens"] = new JsonArray(xboxToken)
                },
                ["RelyingParty"] = "rp://api.minecraftservices.com/",
                ["TokenType"] = "JWT"
            }.ToJsonString(), cancellationToken).ConfigureAwait(false);

        if (xsts.Status == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException(DescribeXstsError(xsts));

        var xstsToken = ReadXboxToken(xsts, "XSTS");
        userHash = ReadUserHash(xsts) ?? userHash
                   ?? throw new InvalidDataException("Xbox 响应缺少用户标识。");

        var login = await JsonHttp.PostJsonAsync(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new JsonObject
            {
                ["identityToken"] = $"XBL3.0 x={userHash};{xstsToken}"
            }.ToJsonString(), cancellationToken).ConfigureAwait(false);

        if (!login.IsSuccess || login.Body is not JsonObject loginBody)
            throw new InvalidOperationException($"Minecraft 登录失败：{login.ErrorMessage}");

        var accessToken = loginBody["access_token"]?.ToString()
                          ?? throw new InvalidDataException("Minecraft 登录响应缺少访问令牌。");
        var expiresIn = loginBody["expires_in"]?.GetValue<int>() ?? 86400;

        var profile = await JsonHttp.GetJsonAsync(
            "https://api.minecraftservices.com/minecraft/profile",
            cancellationToken, accessToken).ConfigureAwait(false);

        if (profile.Status == HttpStatusCode.NotFound)
            throw new InvalidOperationException("该 Microsoft 账户未拥有 Minecraft: Java Edition。");
        if (!profile.IsSuccess || profile.Body is not JsonObject profileBody)
            throw new InvalidOperationException($"获取 Minecraft 档案失败：{profile.ErrorMessage}");

        return new MinecraftProfile(
            profileBody["id"]?.ToString() ?? "",
            profileBody["name"]?.ToString() ?? "",
            accessToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static MicrosoftToken ReadToken(JsonObject body) => new(
        body["access_token"]?.ToString() ?? "",
        body["refresh_token"]?.ToString() ?? "",
        DateTimeOffset.UtcNow.AddSeconds(body["expires_in"]?.GetValue<int>() ?? 3600));

    private static string ReadXboxToken(JsonHttpResponse response, string stage)
    {
        if (!response.IsSuccess || response.Body is not JsonObject body)
            throw new InvalidOperationException($"{stage} 认证失败：{response.ErrorMessage}");
        return body["Token"]?.ToString()
               ?? throw new InvalidDataException($"{stage} 响应缺少令牌。");
    }

    private static string? ReadUserHash(JsonHttpResponse response) =>
        response.Body?["DisplayClaims"]?["xui"]?[0]?["uhs"]?.ToString();

    private static string DescribeXstsError(JsonHttpResponse response)
    {
        var xerr = response.Body?["XErr"]?.ToString();
        return xerr switch
        {
            "2148916233" => "该 Microsoft 账户尚未创建 Xbox 档案，请先登录 Xbox 官网。",
            "2148916235" => "该账户所在地区不支持 Xbox Live。",
            "2148916236" or "2148916237" => "该账户需要完成成人验证。",
            "2148916238" => "未成年账户需要由家庭组添加。",
            _ => $"Xbox 认证失败：{response.ErrorMessage}"
        };
    }
}
