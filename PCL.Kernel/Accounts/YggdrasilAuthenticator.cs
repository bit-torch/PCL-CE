using System.Net;
using System.Text.Json.Nodes;

namespace PCL.Kernel.Accounts;

/// <summary>Yggdrasil / authlib-injector 第三方认证。</summary>
public sealed class YggdrasilAuthenticator
{
    public async Task<LauncherAccount> AuthenticateAsync(
        string apiRoot,
        string username,
        string password,
        string? clientToken = null,
        CancellationToken cancellationToken = default)
    {
        var root = YggdrasilServer.NormalizeApiRoot(apiRoot);
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("请填写 Yggdrasil API Root。", nameof(apiRoot));

        var body = new JsonObject
        {
            ["agent"] = new JsonObject
            {
                ["name"] = "Minecraft",
                ["version"] = 1
            },
            ["username"] = username,
            ["password"] = password,
            ["requestUser"] = true
        };

        if (!string.IsNullOrWhiteSpace(clientToken))
            body["clientToken"] = clientToken;

        var response = await JsonHttp.PostJsonAsync(
            root + "/authserver/authenticate",
            body.ToJsonString(), cancellationToken).ConfigureAwait(false);

        if (response.Status == HttpStatusCode.Forbidden ||
            response.Status == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("用户名或密码错误，或该账户需要额外的安全验证。");

        if (!response.IsSuccess || response.Body is not JsonObject result)
            throw new InvalidOperationException($"第三方登录失败：{response.ErrorMessage}");

        return CreateAccount(root, result);
    }

    public async Task<LauncherAccount> RefreshAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        if (account.Kind != AccountKind.Yggdrasil || string.IsNullOrWhiteSpace(account.ApiRoot))
            throw new InvalidOperationException("该账户不是 Yggdrasil 账户。");

        var root = YggdrasilServer.NormalizeApiRoot(account.ApiRoot);
        var body = new JsonObject
        {
            ["accessToken"] = account.AccessToken,
            ["clientToken"] = account.ClientToken ?? "",
            ["requestUser"] = true
        };

        var response = await JsonHttp.PostJsonAsync(
            root + "/authserver/refresh",
            body.ToJsonString(), cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess || response.Body is not JsonObject result)
            throw new InvalidOperationException($"刷新第三方登录失败：{response.ErrorMessage}");

        return CreateAccount(root, result);
    }

    private static LauncherAccount CreateAccount(string root, JsonObject result)
    {
        var profile = result["selectedProfile"] as JsonObject
                      ?? throw new InvalidOperationException("该第三方账户尚未选择角色。");

        return new LauncherAccount
        {
            Id = profile["id"]?.ToString() ?? "",
            Name = profile["name"]?.ToString() ?? "",
            Kind = AccountKind.Yggdrasil,
            AccessToken = result["accessToken"]?.ToString() ?? "",
            ClientToken = result["clientToken"]?.ToString(),
            ApiRoot = root
        };
    }
}
