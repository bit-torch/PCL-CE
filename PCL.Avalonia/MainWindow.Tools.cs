using Avalonia.Platform.Storage;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Platform;
using PCL.CrossPlatform;

namespace PCL.Avalonia;

public sealed partial class MainWindow
{
    private readonly PortableLobby _lobby = new();
    private Control ToolsPages() => Tabs(("联机", LobbyPage()), ("崩溃分析", DiagnosticsPage()), ("更新", UpdatesPage()), ("帮助与关于", AboutPage()));
    private Control LobbyPage()
    {
        var panel = Page(); panel.Children.Add(Heading("联机大厅"));
        var code = new TextBox { Watermark = "大厅码" }; var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 25565 };
        var directory = new TextBox { Text = _settings.EasyTierDirectory, Watermark = "EasyTier 可执行文件所在目录（留空使用自动安装位置）" };
        var relays = new TextBox { Watermark = "自定义中继地址，每行一个，可留空", AcceptsReturn = true, Height = 75 };
        panel.Children.Add(directory);
        panel.Children.Add(Row(ActionButton("下载当前系统 EasyTier", async token => { await PortableLobby.InstallDependencyAsync(_http, token); Report("联机组件已安装。"); }),
            ActionButton("选择已有 EasyTier", async _ =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new global::Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "选择 EasyTier 目录" });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) { directory.Text = path; _settings.EasyTierDirectory = path; SaveSettings(); }
            })));
        panel.Children.Add(port); panel.Children.Add(code); panel.Children.Add(relays);
        void Configure()
        {
            EasyTierMetadata.DirectoryOverride = string.IsNullOrWhiteSpace(directory.Text) ? null : directory.Text;
            ETRelay.RelayList = (relays.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(url => new ETRelay { Url = url, Name = "Custom", Type = ETRelayType.Custom }).ToList();
        }
        panel.Children.Add(Row(ActionButton("创建大厅", async token => { Configure(); code.Text = await _lobby.HostAsync((int)(port.Value ?? 25565), _profile?.UserName ?? _offlineName.Text ?? "Player", token); Report("大厅已创建，可分享大厅码。"); }),
            ActionButton("加入大厅", async token => { Configure(); var address = await _lobby.JoinAsync(code.Text ?? "", _profile?.UserName ?? _offlineName.Text ?? "Player", token); InstanceOptions.ServerAddress = address; Report("大厅已加入，游戏连接地址：" + address); }),
            ActionButton("复制大厅码", async _ => { if (Clipboard is not null) await Clipboard.SetTextAsync(code.Text); }),
            ActionButton("退出大厅", async _ => { await _lobby.DisposeAsync(); Report("已退出大厅。"); })));
        panel.Children.Add(ActionButton("检测 NAT 与 IPv6", async token =>
        {
            Configure(); var result = await CliNetTest.GetNetStatusAsync(token)
                ?? throw new InvalidOperationException("网络检测未返回有效结果。");
            Report($"UDP NAT：{result.UdpNatType}\nTCP NAT：{result.TcpNatType}\nIPv6：{result.SupportIPv6}");
        }));
        return new ScrollViewer { Content = panel };
    }
    private Control DiagnosticsPage()
    {
        var panel = Page(); panel.Children.Add(Heading("日志与崩溃分析"));
        var content = new TextBox { AcceptsReturn = true, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Height = 200, Watermark = "粘贴游戏日志或载入文件" };
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Height = 180 };
        panel.Children.Add(content);
        panel.Children.Add(Row(ActionButton("载入日志文件", async token => { var file = await PickFile("选择游戏日志或崩溃报告"); if (file is not null) content.Text = await File.ReadAllTextAsync(file, token); }),
            ActionButton("读取最新游戏日志", async token => { content.Text = await File.ReadAllTextAsync(Path.Combine(CurrentDataDirectory, "logs", "latest.log"), token); }),
            ActionButton("分析原因", async token =>
            {
                var text = content.Text ?? ""; var findings = await Task.Run(() => PortableDiagnostics.Analyze(text), token);
                output.Text = findings.Count == 0 ? "未匹配到已知原因。请同时提供 crash-report、hs_err 及 Mod 列表。" : string.Join("\n\n", findings.Select(f => $"{f.Cause} · {f.Confidence}\n{string.Join('\n', f.Evidence)}"));
            })));
        panel.Children.Add(output);
        panel.Children.Add(ActionButton("导出诊断报告", async token => { var target = await PickSave("导出诊断", "diagnostics.txt"); if (target is not null) await File.WriteAllTextAsync(target, output.Text + "\n\n" + content.Text, token); }));
        return new ScrollViewer { Content = panel };
    }
    private Control UpdatesPage()
    {
        var panel = Page(); var description = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var prerelease = new CheckBox { Content = "包含测试版" }; DesktopRelease? release = null; PreparedUpdate? prepared = null;
        var automatic = new CheckBox { Content = "启动时检查更新", IsChecked = _settings.CheckUpdatesOnStartup };
        automatic.IsCheckedChanged += (_, _) => { _settings.CheckUpdatesOnStartup = automatic.IsChecked == true; SaveSettings(); };
        panel.Children.Add(automatic); panel.Children.Add(Heading("启动器更新")); panel.Children.Add(prerelease); panel.Children.Add(description);
        panel.Children.Add(ActionButton("检查最新版本", async token =>
        {
            release = await new DesktopUpdater(_http).LatestAsync(prerelease.IsChecked == true, token);
            description.Text = release.Version + "\n\n" + release.Notes;
        }));
        panel.Children.Add(ActionButton("下载并准备更新", async token =>
        {
            if (release is null) throw new InvalidOperationException("请先检查版本。");
            var archive = Path.Combine(PCL.Core.App.Paths.Temp, Guid.NewGuid().ToString("N") + ".tar.gz");
            try
            {
                await new DesktopUpdater(_http).DownloadAsync(release, archive, token);
                prepared = await DesktopUpdater.PrepareAsync(archive, AppContext.BaseDirectory, PCL.Core.App.Paths.Temp, token);
                Report("更新已准备好。点击安装更新后会退出启动器，替换程序并重新启动；旧程序将保留在更新目录备份中。");
            }
            finally { if (File.Exists(archive)) File.Delete(archive); }
        }));
        panel.Children.Add(ActionButton("安装更新并重启", async _ =>
        {
            EnsureGameStopped(); if (prepared is null) throw new InvalidOperationException("请先下载并准备更新。");
            await _lobby.DisposeAsync();
            var helper = Process.Start(DesktopUpdater.CreateApplyProcess(prepared, Environment.ProcessId)) ?? throw new InvalidOperationException("无法启动更新助手。");
            global::Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        }));
        return new ScrollViewer { Content = panel };
    }
    private Control AboutPage()
    {
        var panel = Page(); panel.Children.Add(Heading("PCL Community Edition"));
        panel.Children.Add(Label("上游作者：龙腾猫跃 / PCL Community\n本仓库：bit-torch/PCL-CE\n跨平台界面：Avalonia\n媒体后端：LibVLCSharp / LibVLC（动态链接）"));
        panel.Children.Add(Label($"运行环境：{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\n{System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n配置：{DesktopSettings.FilePath}"));
        panel.Children.Add(Row(ActionButton("项目主页", async _ => await Launcher.LaunchUriAsync(new Uri("https://github.com/bit-torch/PCL-CE"))),
            ActionButton("使用文档", async _ => await Launcher.LaunchUriAsync(new Uri("https://docs.pclc.cc/ce/"))),
            ActionButton("问题反馈", async _ => await Launcher.LaunchUriAsync(new Uri("https://github.com/bit-torch/PCL-CE/pulls")))));
        panel.Children.Add(ActionButton("查看许可证", async _ => await Launcher.LaunchFileInfoAsync(new FileInfo(Path.Combine(AppContext.BaseDirectory, "LICENSE")))));
        return new ScrollViewer { Content = panel };
    }
    private Control JavaDownloadPage()
    {
        var panel = Page(); var major = new ComboBox { ItemsSource = new[] { 8, 11, 17, 21, 25 }, SelectedItem = 21 };
        panel.Children.Add(Heading("Java 运行时")); panel.Children.Add(major);
        panel.Children.Add(ActionButton("下载并选择 Java", async token =>
        {
            _java.Text = await new JavaRuntimeInstaller(_http).InstallAsync((int)major.SelectedItem!, Path.Combine(PCL.Core.App.Paths.SharedLocalData, "Java"), Progress(), token);
            SaveSettings(); Report("Java 安装完成。");
        }));
        panel.Children.Add(ActionButton("扫描本地 Java", ScanJava));
        return new ScrollViewer { Content = panel };
    }
}
