using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Models;
using PCL.CrossPlatform;

namespace PCL.Avalonia;

public sealed partial class MainWindow
{
    private Control AccountPages() => Tabs(("账户列表", AccountsListPage()), ("微软登录", AccountPage()), ("第三方登录", ThirdPartyPage()), ("皮肤与披风", SkinPage()));
    private Control AccountsListPage()
    {
        var panel = Page(); var list = new ListBox { Height = 280 };
        void Refresh() { list.ItemsSource = _settings.Accounts().Select(p => new AccountItem(p)).ToArray(); }
        panel.Children.Add(Heading("管理账户")); panel.Children.Add(ActionButton("刷新账户", _ => { Refresh(); return Task.CompletedTask; })); panel.Children.Add(list);
        panel.Children.Add(Row(ActionButton("使用此账户", _ =>
        {
            var selected = list.SelectedItem as AccountItem ?? throw new InvalidOperationException("请选择账户。");
            _settings.SelectedAccountId = selected.Profile.ProfileId; _profile = selected.Profile; SaveSettings(); RefreshAccount(); Report("当前账户：" + _profile.UserName); return Task.CompletedTask;
        }), ActionButton("移除所选账户", _ =>
        {
            if (list.SelectedItem is not AccountItem selected) throw new InvalidOperationException("请选择账户。");
            _settings.SelectedAccountId = selected.Profile.ProfileId; _settings.RemoveSelectedAccount(); _profile = _settings.SelectedAccount(); SaveSettings(); RefreshAccount(); Refresh(); return Task.CompletedTask;
        })));
        return new ScrollViewer { Content = panel };
    }
    private sealed record AccountItem(McProfile Profile) { public override string ToString() => $"{Profile.UserName} · {Profile.ProfileType} · {Profile.Server}"; }
    private IAuthenticateProvider AccountProvider(McProfile profile) => profile.ProfileType switch
    {
        ProfileType.Microsoft => new MicrosoftProvider(_clientId.Text),
        ProfileType.Authlib => new AuthlibProvider(profile.Server!),
        ProfileType.YggdrasilConnect => new YggdrasilConnectProvider(profile.DiscoveryAddress!, _settings.ProviderClientIds.GetValueOrDefault(profile.DiscoveryAddress ?? ""), profile.Server),
        _ => throw new InvalidOperationException("此账户不需要在线认证。")
    };
    private Control ThirdPartyPage()
    {
        var panel = Page(); var server = new TextBox { Watermark = "Yggdrasil API 地址（https://…/api/yggdrasil）" };
        var username = new TextBox { Watermark = "用户名 / 邮箱" }; var password = new TextBox { Watermark = "密码", PasswordChar = '●' };
        var discovery = new TextBox { Watermark = "Yggdrasil Connect 发现地址" }; var clientId = new TextBox { Watermark = "应用客户端 ID（如服务提供方要求）" };
        panel.Children.Add(Heading("第三方认证")); panel.Children.Add(server); panel.Children.Add(username); panel.Children.Add(password);
        panel.Children.Add(ActionButton("使用密码登录", async token =>
        {
            RequireHttps(server.Text);
            var result = await new AuthlibProvider(server.Text!).AuthenticateAsync(new AuthenticationRequest
            { Username = username.Text, Password = password.Text, ProfileSelector = SelectAuthenticationProfile }, token);
            password.Text = ""; StoreAuthenticated(result);
        }));
        panel.Children.Add(discovery); panel.Children.Add(clientId);
        panel.Children.Add(ActionButton("使用设备码登录", async token =>
        {
            RequireHttps(discovery.Text);
            var provider = new YggdrasilConnectProvider(discovery.Text!, clientId.Text, server.Text);
            await provider.InitializeAsync(token);
            var result = await provider.AuthenticateAsync(new AuthenticationRequest { DeviceCodeHandler = PollThirdPartyDevice, ProfileSelector = SelectAuthenticationProfile }, token);
            _settings.ProviderClientIds[discovery.Text!] = clientId.Text ?? ""; StoreAuthenticated(result);
        }));
        return new ScrollViewer { Content = panel };
    }
    private void StoreAuthenticated(AuthenticationResult result)
    {
        _settings.StoreAccount(result); _profile = _settings.SelectedAccount(); SaveSettings(); RefreshAccount(); Report("登录成功：" + result.UserName);
    }
    private async Task<AuthenticationCandidate?> SelectAuthenticationProfile(IReadOnlyList<AuthenticationCandidate> profiles, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var list = new ComboBox { ItemsSource = profiles, SelectedIndex = 0 };
            var button = new Button { Content = "选择此角色" }; var panel = new StackPanel { Margin = new global::Avalonia.Thickness(22), Spacing = 16 };
            panel.Children.Add(list); panel.Children.Add(button); var window = new Window { Title = "选择角色", Width = 430, Height = 190, Content = panel };
            button.Click += (_, _) => window.Close(list.SelectedItem as AuthenticationCandidate);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            return await window.ShowDialog<AuthenticationCandidate?>(this);
        });
    }
    private async Task<PCL.Core.Minecraft.IdentityModel.OAuth.AuthorizeResult?> PollThirdPartyDevice(DeviceCodeAuthenticationContext context, CancellationToken token)
    {
        await Dispatcher.UIThread.InvokeAsync(() => Report($"登录地址：{context.Data.VerificationUri}\n设备码：{context.Data.UserCode}"));
        await Dispatcher.UIThread.InvokeAsync(async () => await Launcher.LaunchUriAsync(new Uri(context.Data.VerificationUriComplete ?? context.Data.VerificationUri!)));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(context.Data.ExpiresIn ?? 900); var interval = Math.Max(context.Data.Interval ?? 5, 1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), token); var result = await context.PollAsync(token);
            if (result is null) continue; if (!result.IsError) return result;
            if (result.Error == "slow_down") { interval += 5; continue; }
            if (result.Error != "authorization_pending") throw new InvalidOperationException(result.ErrorDescription ?? result.Error);
        }
        throw new TimeoutException("设备授权超时。");
    }
    private static void RequireHttps(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("认证地址必须使用 HTTPS。");
    }
    private async Task<LaunchOptions> BuildLaunchOptions(CancellationToken token)
    {
        var preferences = InstanceOptions;
        var jvm = GameProcess.SplitLegacyArguments(preferences.JvmArguments).ToList();
        if (_useAccount.IsChecked == true && _profile is { ProfileType: ProfileType.Authlib or ProfileType.YggdrasilConnect } profile)
        {
            var server = profile.Server ?? throw new InvalidOperationException("第三方账户缺少认证服务器。");
            if (server.EndsWith("/authserver")) server = server[..^11]; RequireHttps(server);
            var metadata = JsonNode.Parse(await _http.GetStringAsync("https://authlib-injector.yushi.moe/artifact/latest.json", token))!;
            var hash = metadata["checksums"]?["sha256"]!.ToString() ?? throw new InvalidDataException("认证补丁缺少校验信息。");
            var path = Path.Combine(PCL.Core.App.Paths.SharedLocalData, "authlib-injector-" + hash + ".jar");
            await new GameInstaller(_http).DownloadAsync(metadata["download_url"]!.ToString(), path, null, null, token);
            await using var input = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("认证补丁校验失败。");
            jvm.Add("-javaagent:" + path + "=" + server); jvm.Add("-Dauthlibinjector.side=client");
        }
        var environment = new Dictionary<string, string>();
        foreach (var line in preferences.EnvironmentVariables.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = line.Trim().Split('=', 2); if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0])) throw new InvalidDataException("环境变量格式应为 NAME=VALUE。");
            environment[pair[0]] = pair[1];
        }
        return new LaunchOptions { GameDirectory = CurrentDataDirectory, JvmArguments = jvm,
            GameArguments = GameProcess.SplitLegacyArguments(preferences.GameArguments).ToArray(), Environment = environment,
            Width = preferences.Width, Height = preferences.Height, ServerAddress = preferences.ServerAddress };
    }
    private Control SkinPage()
    {
        var panel = Page(); var model = new ComboBox { ItemsSource = new[] { "classic", "slim" }, SelectedIndex = 0 }; var capes = new ComboBox(); JsonObject? current = null;
        McProfile Profile() => _profile is { ProfileType: ProfileType.Microsoft } p ? p : throw new InvalidOperationException("请先选择已登录的微软账户。");
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content, CancellationToken token)
        {
            var request = new HttpRequestMessage(method, "https://api.minecraftservices.com/" + path) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Profile().AccessToken);
            var response = await _http.SendAsync(request, token); request.Dispose(); response.EnsureSuccessStatusCode(); return response;
        }
        panel.Children.Add(Heading("皮肤与披风"));
        panel.Children.Add(ActionButton("读取在线皮肤和披风", async token =>
        {
            using var response = await Send(HttpMethod.Get, "minecraft/profile", null, token);
            current = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
            capes.ItemsSource = current["capes"]!.AsArray().Select(c => new Cape(c!["id"]!.ToString(), c["alias"]?.ToString() ?? c["id"]!.ToString())).ToArray(); capes.SelectedIndex = 0;
            Report("已读取在线外观。");
        })); panel.Children.Add(model);
        panel.Children.Add(Row(ActionButton("上传皮肤", async token =>
        {
            var file = await PickFile("选择皮肤 PNG"); if (file is null) return;
            using var form = new MultipartFormDataContent(); form.Add(new StringContent(model.SelectedItem!.ToString()!), "variant");
            form.Add(new StreamContent(File.OpenRead(file)), "file", Path.GetFileName(file));
            using var response = await Send(HttpMethod.Post, "minecraft/profile/skins", form, token); Report("皮肤已更新。");
        }), ActionButton("重置皮肤", async token => { using var response = await Send(HttpMethod.Delete, "minecraft/profile/skins/active", null, token); Report("皮肤已重置。"); }),
            ActionButton("下载当前皮肤", async token =>
            {
                var url = current?["skins"]?.AsArray().FirstOrDefault()?["url"]?.ToString() ?? throw new InvalidOperationException("请先读取在线皮肤。");
                var target = await PickSave("保存皮肤", "skin.png"); if (target is null) return;
                var bytes = await _http.GetByteArrayAsync(url, token); await File.WriteAllBytesAsync(target, bytes, token);
            })));
        panel.Children.Add(capes); panel.Children.Add(Row(ActionButton("使用所选披风", async token =>
        {
            var cape = capes.SelectedItem as Cape ?? throw new InvalidOperationException("请选择披风。");
            using var response = await Send(HttpMethod.Put, "minecraft/profile/capes/active", System.Net.Http.Json.JsonContent.Create(new { capeId = cape.Id }), token); Report("披风已切换。");
        }), ActionButton("隐藏披风", async token => { using var response = await Send(HttpMethod.Delete, "minecraft/profile/capes/active", null, token); Report("披风已隐藏。"); })));
        return new ScrollViewer { Content = panel };
    }
    private sealed record Cape(string Id, string Name) { public override string ToString() => Name; }
}
