using System.Diagnostics;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using PCL.Core.Minecraft.Java;
using PCL.Core.Minecraft.Java.Parser;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Models;
using PCL.CrossPlatform;

namespace PCL.Avalonia;

public sealed class MainWindow : Window
{
    private readonly DesktopSettings _settings;
    private readonly bool _canSave;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly List<Control> _operationControls = [];
    private readonly ComboBox _installed = new() { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _available = new() { Height = 280 };
    private readonly TextBox _root = new();
    private readonly TextBox _java = new();
    private readonly TextBox _offlineName = new();
    private readonly TextBox _clientId = new();
    private readonly NumericUpDown _memory = new() { Minimum = 256, Maximum = 131072, Increment = 512 };
    private readonly CheckBox _useAccount = new() { Content = "使用已登录账户" };
    private readonly TextBlock _account = new() { Text = "尚未登录", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Text = "就绪", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 300 };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 5 };
    private readonly Button _cancel = new() { Content = "取消任务", IsEnabled = false };
    private readonly Button _stop = new() { Content = "停止游戏", IsEnabled = false };
    private CancellationTokenSource? _operation;
    private Process? _game;
    private McProfile? _profile;

    public MainWindow(DesktopSettings? settings = null)
    {
        Title = "PCL Community Edition · 跨平台预览";
        Width = 960; Height = 720; MinWidth = 760; MinHeight = 580;
        string? startupError = null;
        try { _settings = settings ?? DesktopSettings.Load(); _canSave = true; }
        catch (Exception ex) { _settings = new(); startupError = "设置文件无法读取，已保留原文件：" + ex.Message; }
        _root.Text = _settings.GameDirectory;
        _java.Text = _settings.JavaPath;
        _offlineName.Text = _settings.OfflineName;
        _clientId.Text = _settings.MicrosoftClientId;
        _memory.Value = Math.Clamp(_settings.MemoryMb, 256, 131072);
        ApplyTheme(_settings.DarkTheme);
        try { _profile = _settings.LoadAccount(); }
        catch (Exception ex) { startupError = "已保存的账户无法解密，原数据已保留：" + ex.Message; }
        RefreshAccount();

        var header = new StackPanel { Margin = new Thickness(26, 22, 26, 12), Spacing = 6 };
        header.Children.Add(new TextBlock { Text = "PCL Community Edition", FontSize = 27, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = $"跨平台预览 · {RuntimePlatform.Current.Os} / {RuntimePlatform.Current.Architecture}", Opacity = .65 });
        var tabs = new TabControl { Margin = new Thickness(20, 0) };
        tabs.Items.Add(new TabItem { Header = "启动", Content = LaunchPage() });
        tabs.Items.Add(new TabItem { Header = "下载", Content = DownloadPage() });
        tabs.Items.Add(new TabItem { Header = "账户", Content = AccountPage() });
        tabs.Items.Add(new TabItem { Header = "设置", Content = SettingsPage() });
        tabs.Items.Add(new TabItem { Header = "日志", Content = new ScrollViewer { Content = _log, Margin = new Thickness(6, 16) } });
        var footer = new StackPanel { Margin = new Thickness(26, 12, 26, 18), Spacing = 10 };
        footer.Children.Add(_progress);
        footer.Children.Add(_status);
        footer.Children.Add(Row(_cancel, _stop));
        _cancel.Click += (_, _) => _operation?.Cancel();
        _stop.Click += (_, _) =>
        {
            try { if (_game is { HasExited: false }) _game.Kill(entireProcessTree: true); }
            catch (Exception ex) { Report(ex.Message); }
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        layout.Children.Add(header); Grid.SetRow(tabs, 1); layout.Children.Add(tabs);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
        Closing += (_, e) =>
        {
            if (_game is { HasExited: false })
            {
                e.Cancel = true; Report("游戏正在运行，请先退出游戏或点击“停止游戏”。"); return;
            }
            _operation?.Cancel();
        };
        Closed += (_, _) => { _http.Dispose(); _game?.Dispose(); };
        Opened += async (_, _) =>
        {
            RefreshInstalled();
            if (startupError is not null) Report(startupError);
            else if (_java.Text is "java" or "java.exe") await RunOperation(ScanJava);
        };
    }

    private Control LaunchPage()
    {
        var panel = Page();
        panel.Children.Add(Heading("开始游戏"));
        panel.Children.Add(Label("选择本地版本")); panel.Children.Add(_installed);
        panel.Children.Add(Row(ActionButton("刷新版本", _ => { RefreshInstalled(); return Task.CompletedTask; }),
            ActionButton("打开游戏目录", async _ => await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(GameRoot)))));
        panel.Children.Add(Label("离线用户名")); panel.Children.Add(_offlineName); panel.Children.Add(_useAccount);
        panel.Children.Add(Row(ActionButton("启动游戏", Launch), ActionButton("检查并修复文件", Repair)));
        panel.Children.Add(new TextBlock
        {
            Text = "支持原版安装与标准版本 JSON 的启动。已安装的 Fabric / Forge 版本可尝试启动；整合包安装、联机与旧版自定义主页尚在迁移。",
            TextWrapping = TextWrapping.Wrap, Opacity = .65, Margin = new Thickness(0, 18, 0, 0)
        });
        return new ScrollViewer { Content = panel };
    }

    private Control DownloadPage()
    {
        var panel = Page();
        panel.Children.Add(Heading("安装 Minecraft"));
        var snapshots = new CheckBox { Content = "包含快照版本" };
        panel.Children.Add(snapshots);
        panel.Children.Add(ActionButton("获取版本列表", async token =>
        {
            var versions = await new GameInstaller(_http).GetVersionsAsync(token);
            _available.ItemsSource = versions.Where(v => snapshots.IsChecked == true || v.Type == "release").ToArray();
            _available.SelectedIndex = 0; Report($"已获取 {_available.ItemCount} 个版本。");
        }));
        panel.Children.Add(_available);
        panel.Children.Add(ActionButton("安装所选版本", async token =>
        {
            if (_available.SelectedItem is not AvailableVersion version) throw new InvalidOperationException("请先选择要安装的版本。");
            SaveSettings();
            await new GameInstaller(_http).InstallAsync(GameRoot, version, Progress(), token);
            _settings.SelectedVersion = version.Id; RefreshInstalled(); Report($"{version.Id} 安装完成。");
        }));
        return new ScrollViewer { Content = panel };
    }

    private Control AccountPage()
    {
        var panel = Page(); panel.Children.Add(Heading("账户")); panel.Children.Add(_account);
        panel.Children.Add(Label("Microsoft OAuth 客户端 ID")); panel.Children.Add(_clientId);
        panel.Children.Add(new TextBlock { Text = "使用已获 Minecraft API 权限的应用客户端 ID。授权时会显示设备码，并在浏览器中完成登录。", TextWrapping = TextWrapping.Wrap, Opacity = .65 });
        panel.Children.Add(Row(ActionButton("微软账户登录", Login), ActionButton("退出账户", _ =>
        {
            _profile = null; _settings.EncryptedAccount = null; SaveSettings(); RefreshAccount(); return Task.CompletedTask;
        })));
        return new ScrollViewer { Content = panel };
    }

    private Control SettingsPage()
    {
        var panel = Page(); panel.Children.Add(Heading("启动设置"));
        panel.Children.Add(Label("Minecraft 目录")); panel.Children.Add(_root);
        panel.Children.Add(ActionButton("选择文件夹", async _ =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择 Minecraft 目录", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) { _root.Text = path; RefreshInstalled(); }
        }));
        panel.Children.Add(Label("Java 可执行文件")); panel.Children.Add(_java);
        panel.Children.Add(Row(ActionButton("自动检测 Java", ScanJava), ActionButton("选择 Java", async _ =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择 Java 可执行文件", AllowMultiple = false });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) _java.Text = path;
        })));
        panel.Children.Add(Label("最大内存（MiB）")); panel.Children.Add(_memory);
        var dark = new CheckBox { Content = "深色主题", IsChecked = _settings.DarkTheme };
        dark.IsCheckedChanged += (_, _) => { _settings.DarkTheme = dark.IsChecked == true; ApplyTheme(_settings.DarkTheme); };
        panel.Children.Add(dark);
        panel.Children.Add(ActionButton("保存设置", _ => { SaveSettings(); Report("设置已保存。"); return Task.CompletedTask; }));
        return new ScrollViewer { Content = panel };
    }

    private async Task Login(CancellationToken token)
    {
        var provider = new MicrosoftProvider(_clientId.Text);
        var result = await provider.AuthenticateAsync(new AuthenticationRequest
        {
            DeviceCodeHandler = async (context, ct) =>
            {
                var data = context.Data;
                var uri = new Uri(data.VerificationUriComplete ?? data.VerificationUri!);
                var code = $"登录地址：{uri}\n设备码：{data.UserCode}";
                await Dispatcher.UIThread.InvokeAsync(() => Report(code));
                await Dispatcher.UIThread.InvokeAsync(async () => { await Launcher.LaunchUriAsync(uri); });
                var until = DateTimeOffset.UtcNow.AddSeconds(data.ExpiresIn ?? 900);
                var interval = Math.Max(data.Interval ?? 5, 1);
                while (DateTimeOffset.UtcNow < until)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), ct);
                    var auth = await context.PollAsync(ct);
                    if (auth is null) continue;
                    if (!auth.IsError) return auth;
                    if (auth.Error == "slow_down") { interval += 5; continue; }
                    if (auth.Error == "authorization_pending") continue;
                    throw new InvalidOperationException(auth.ErrorDescription ?? auth.Error);
                }
                throw new TimeoutException("设备授权已超时，请重新登录。");
            }
        }, token);
        _settings.StoreAccount(result); _profile = _settings.LoadAccount(); SaveSettings(); RefreshAccount();
        Report("登录成功。");
    }

    private async Task Launch(CancellationToken token)
    {
        if (_game is { HasExited: false }) throw new InvalidOperationException("游戏已经在运行。");
        var id = SelectedVersion; SaveSettings();
        var javaPath = _java.Text?.Trim() ?? "";
        if (!Path.IsPathFullyQualified(javaPath) || !File.Exists(javaPath))
            throw new InvalidOperationException("请在设置中选择 Java，或使用自动检测。");
        IJavaParser parser = OperatingSystem.IsWindows() ? new PeHeaderParser() : new ReleaseFileJavaParser();
        var java = parser.Parse(javaPath).Match(value => value, error => throw error);
        var manifest = GameManifest.Load(GameRoot, id);
        var required = manifest["javaVersion"]?["majorVersion"]?.GetValue<int>() ?? 8;
        if (java.MajorVersion < required) throw new InvalidOperationException($"此版本至少需要 Java {required}，当前为 {java.MajorVersion}。");
        LaunchAccount account;
        if (_useAccount.IsChecked == true)
        {
            var profile = _profile ?? throw new InvalidOperationException("请先登录账户。");
            if (profile.IsExpired)
            {
                var refreshed = await new MicrosoftProvider(_clientId.Text).RefreshAsync(profile, token);
                _settings.StoreAccount(refreshed); _profile = _settings.LoadAccount(); SaveSettings(); RefreshAccount();
                profile = _profile!;
            }
            account = new(profile.UserName, profile.Uuid, profile.AccessToken);
        }
        else account = LaunchAccount.Offline(_offlineName.Text ?? "");
        token.ThrowIfCancellationRequested();
        var root = GameRoot;
        var memory = (int)(_memory.Value ?? 2048);
        _game = await Task.Run(() => new GameProcess().Start(root, id, javaPath, account,
            memory, line => Dispatcher.UIThread.Post(() => AppendLog(line))), token);
        _stop.IsEnabled = true; Report($"{id} 已启动。");
        _ = ObserveGame(_game);
    }

    private async Task ObserveGame(Process process)
    {
        await process.WaitForExitAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Report($"游戏已退出，退出码：{process.ExitCode}"); _stop.IsEnabled = false;
            _game = null; process.Dispose();
        });
    }

    private async Task Repair(CancellationToken token)
    {
        SaveSettings(); await new GameInstaller(_http).RepairAsync(GameRoot, SelectedVersion, Progress(), token);
        Report("游戏文件检查完成。");
    }

    private async Task ScanJava(CancellationToken token)
    {
        var paths = await Task.Run(() =>
        {
            var results = new List<string>();
            new PathEnvironmentScanner().Scan(results); new UnixJavaScanner().Scan(results);
            if (OperatingSystem.IsWindows()) { new RegistryJavaScanner().Scan(results); new DefaultPathsScanner().Scan(results); }
            IJavaParser parser = OperatingSystem.IsWindows() ? new PeHeaderParser() : new ReleaseFileJavaParser();
            return results.Distinct().Select(path => parser.Parse(path).Match<JavaInstallation?>(value => value, _ => null))
                .Where(value => value is not null).OrderByDescending(value => value!.Version).ToArray();
        }, token);
        token.ThrowIfCancellationRequested();
        if (paths.Length == 0) throw new InvalidOperationException("未找到可用 Java，请安装 JDK 后手动选择可执行文件。");
        _java.Text = paths[0]!.JavaExePath;
        Report($"已找到 {paths.Length} 个 Java，选用 {paths[0]}");
    }

    private void RefreshInstalled()
    {
        try
        {
            var versions = Path.Combine(GameRoot, "versions");
            var ids = Directory.Exists(versions) ? Directory.GetDirectories(versions).Select(Path.GetFileName)
                .Where(id => id is not null && File.Exists(GameManifest.VersionPath(GameRoot, id))).OrderDescending().ToArray() : [];
            _installed.ItemsSource = ids;
            _installed.SelectedItem = ids.FirstOrDefault(id => id == _settings.SelectedVersion) ?? ids.FirstOrDefault();
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private string GameRoot => Path.GetFullPath(string.IsNullOrWhiteSpace(_root.Text) ? _settings.GameDirectory : _root.Text.Trim());
    private string SelectedVersion => _installed.SelectedItem as string ?? throw new InvalidOperationException("请先安装或选择一个游戏版本。");
    private IProgress<InstallProgress> Progress() => new Progress<InstallProgress>(value =>
    {
        _progress.Value = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
        _status.Text = $"{value.Message} · {value.Completed}/{value.Total}";
    });

    private void SaveSettings()
    {
        if (!_canSave) throw new InvalidOperationException("原设置文件无法读取。请修复设置文件后重启，以免覆盖数据。");
        _settings.GameDirectory = GameRoot; _settings.JavaPath = _java.Text ?? "";
        _settings.MemoryMb = (int)(_memory.Value ?? 2048); _settings.OfflineName = _offlineName.Text ?? "Player";
        _settings.MicrosoftClientId = _clientId.Text ?? ""; _settings.SelectedVersion = _installed.SelectedItem as string;
        _settings.Save();
    }

    private void RefreshAccount()
    {
        _account.Text = _profile is null ? "尚未登录，可使用离线用户名启动游戏。" : $"已登录：{_profile.UserName}";
        _useAccount.IsChecked = _profile is not null;
    }
    private static void ApplyTheme(bool dark) => Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    private void Report(string text) { _status.Text = text; AppendLog(text); }
    private void AppendLog(string text)
    {
        var combined = (_log.Text ?? "") + text + Environment.NewLine;
        _log.Text = combined.Length > 40000 ? combined[^30000..] : combined;
        _log.CaretIndex = _log.Text.Length;
    }

    private async Task RunOperation(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        using var source = new CancellationTokenSource(); _operation = source;
        foreach (var control in _operationControls) control.IsEnabled = false;
        _cancel.IsEnabled = true; _progress.Value = 0;
        try { await action(source.Token); }
        catch (OperationCanceledException) { Report("任务已取消。"); }
        catch (Exception ex) { Report("操作失败：" + ex.Message); }
        finally
        {
            _operation = null; _cancel.IsEnabled = false;
            foreach (var control in _operationControls) control.IsEnabled = true;
        }
    }

    private Button ActionButton(string text, Func<CancellationToken, Task> action)
    {
        var button = new Button { Content = text };
        if (text is "启动游戏" or "安装所选版本") button.Classes.Add("accent");
        _operationControls.Add(button);
        button.Click += async (_, _) => await RunOperation(action); return button;
    }
    private static StackPanel Page() => new() { Margin = new Thickness(6, 20), Spacing = 12, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 21, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 6, 0, 0), Opacity = .75 };
    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var child in children) row.Children.Add(child); return row;
    }
}
