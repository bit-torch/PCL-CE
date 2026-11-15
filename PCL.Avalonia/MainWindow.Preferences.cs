using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LibVLCSharp.Avalonia;
using PCL.Core.IO.Net.Http;
using PCL.CrossPlatform;
using PCL.Core.Platform;
using PCL.Core.Utils.Secret;

namespace PCL.Avalonia;

public sealed partial class MainWindow
{
    private readonly DesktopMediaPlayer _mediaPlayer = new();
    private readonly Image _background = new() { Stretch = Stretch.UniformToFill, Opacity = .18, IsHitTestVisible = false };
    private Bitmap? _backgroundBitmap;
    private readonly DesktopLocalization _localization = new();
    private Control PreferencePages() => Tabs(("常规", SettingsPage()), ("下载与网络", NetworkPage()), ("外观与媒体", AppearancePage()), ("语言", LanguagePage()));
    private Control NetworkPage()
    {
        var panel = Page(); var mirror = new TextBox { Text = _settings.DownloadMirror, Watermark = "BMCLAPI 兼容镜像地址（留空为官方源）" };
        var key = new TextBox { PasswordChar = '●', Watermark = "CurseForge API 密钥（不修改时留空）" };
        var proxyMode = new ComboBox { ItemsSource = Enum.GetValues<HttpProxyManager.ProxyMode>(), SelectedItem = _settings.ProxyMode };
        var proxy = new TextBox { Text = _settings.ProxyAddress, Watermark = "代理地址，例如 http://127.0.0.1:7890" };
        panel.Children.Add(Heading("下载源与代理")); panel.Children.Add(mirror); panel.Children.Add(key); panel.Children.Add(proxyMode); panel.Children.Add(proxy);
        panel.Children.Add(ActionButton("保存下载与网络设置", _ =>
        {
            DownloadRoutingHandler.Map(new Uri("https://piston-meta.mojang.com/test"), mirror.Text);
            _settings.DownloadMirror = mirror.Text ?? "";
            if (!string.IsNullOrWhiteSpace(key.Text)) { _settings.EncryptedCurseForgeKey = EncryptHelper.SecretEncrypt(key.Text); key.Text = ""; }
            _settings.ProxyMode = (HttpProxyManager.ProxyMode)proxyMode.SelectedItem!; _settings.ProxyAddress = proxy.Text ?? "";
            ApplyProxySettings(); SaveSettings(); Report("网络设置已保存，新请求将使用新设置。"); return Task.CompletedTask;
        }));
        panel.Children.Add(ActionButton("清除 CurseForge 密钥", _ => { _settings.EncryptedCurseForgeKey = null; SaveSettings(); return Task.CompletedTask; }));
        return new ScrollViewer { Content = panel };
    }
    private void ApplyProxySettings()
    {
        var proxy = HttpProxyManager.Instance;
        if (_settings.ProxyMode == HttpProxyManager.ProxyMode.CustomProxy)
            proxy.CustomProxyAddress = Uri.TryCreate(_settings.ProxyAddress, UriKind.Absolute, out var uri) ? uri : throw new InvalidOperationException("代理地址无效。");
        proxy.Mode = _settings.ProxyMode;
    }
    private Control AppearancePage()
    {
        var panel = Page(); panel.Children.Add(Heading("外观与媒体"));
        panel.Children.Add(Row(ActionButton("选择背景图片", async _ =>
        {
            var file = await PickFile("选择背景图片"); if (file is null) return; _settings.BackgroundImage = file; ApplyBackground(); SaveSettings();
        }), ActionButton("清除背景", _ => { _settings.BackgroundImage = null; ApplyBackground(); SaveSettings(); return Task.CompletedTask; })));
        var vlc = new TextBox { Text = _settings.VlcLibraryDirectory, Watermark = "libvlc 所在目录（使用系统 VLC 时可留空）" };
        var loop = new CheckBox { Content = "循环播放", IsChecked = _settings.LoopMusic };
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = _settings.MusicVolume };
        volume.ValueChanged += (_, _) => _mediaPlayer.Volume((int)volume.Value);
        panel.Children.Add(vlc); panel.Children.Add(loop); panel.Children.Add(Label("媒体音量")); panel.Children.Add(volume);
        void Configure() { _settings.VlcLibraryDirectory = vlc.Text; _settings.MusicVolume = (int)volume.Value; _settings.LoopMusic = loop.IsChecked == true; _mediaPlayer.Initialize(vlc.Text); }
        panel.Children.Add(Row(ActionButton("选择并播放音乐", async _ => { var file = await PickFile("选择音乐文件"); if (file is null) return; Configure(); _settings.MusicFile = file; _mediaPlayer.Play(file, _settings.MusicVolume, _settings.LoopMusic); SaveSettings(); }),
            ActionButton("暂停 / 继续", _ => { _mediaPlayer.Pause(); return Task.CompletedTask; }), ActionButton("停止播放", _ => { _mediaPlayer.Stop(); return Task.CompletedTask; })));
        panel.Children.Add(ActionButton("选择并播放背景视频", async _ =>
        {
            var file = await PickFile("选择视频文件"); if (file is null) return; Configure();
            var layout = Content as Control; Content = null;
            if (layout is VideoView previous) { layout = previous.Content as Control; previous.Content = null; previous.MediaPlayer = null;  }
            var video = new VideoView { MediaPlayer = _mediaPlayer.Player, Content = layout }; Content = video;
            _mediaPlayer.Play(file, _settings.MusicVolume, _settings.LoopMusic);
        }));
        panel.Children.Add(ActionButton("保存媒体设置", _ => { _settings.VlcLibraryDirectory = vlc.Text; _settings.MusicVolume = (int)volume.Value; _settings.LoopMusic = loop.IsChecked == true; SaveSettings(); return Task.CompletedTask; }));
        return new ScrollViewer { Content = panel };
    }
    private void ApplyBackground()
    {
        _background.Source = null; _backgroundBitmap?.Dispose(); _backgroundBitmap = null;
        if (_settings.BackgroundImage is { } file && File.Exists(file)) { _backgroundBitmap = new Bitmap(file); _background.Source = _backgroundBitmap; }
    }
    private Control LanguagePage()
    {
        var panel = Page(); var language = new ComboBox { ItemsSource = new[] { "zh-CN", "zh-TW", "en-US", "en-GB", "ja-JP", "fr-FR", "es-ES" }, SelectedItem = _settings.Language };
        panel.Children.Add(Heading("语言与区域")); panel.Children.Add(language);
        panel.Children.Add(ActionButton("保存语言", _ =>
        {
            _settings.Language = language.SelectedItem!.ToString()!; _localization.ApplyLanguageResources("zh-CN", _settings.Language); PlatformServices.Localization = _localization;
            TranslateVisuals(this); SaveSettings(); Report("语言设置已保存。"); return Task.CompletedTask;
        }));
        return new ScrollViewer { Content = panel };
    }
    private void TranslateVisuals(Control control)
    {
        if (control is TextBlock text) text.Text = _localization.Translate(text.Text ?? "");
        if (control is ContentControl { Content: string value } content) content.Content = _localization.Translate(value);
        if (control is HeaderedContentControl { Header: string header } h) h.Header = _localization.Translate(header);
        foreach (var child in global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(control).OfType<Control>()) TranslateVisuals(child);
    }
    private Control HomepagePage() => Tabs(("主页", CustomHomepagePage()), ("Minecraft 新闻", NewsPage()));
    private Control CustomHomepagePage()
    {
        var panel = Page(); var content = new StackPanel { Spacing = 14 };
        panel.Children.Add(Heading("自定义主页"));
        panel.Children.Add(Row(ActionButton("载入自定义主页", async token =>
        {
            var file = await PickFile("选择主页 XAML"); if (file is null) return;
            var xml = await File.ReadAllTextAsync(file, token);
            var control = CustomHomeRenderer.Render(xml, uri => _ = Launcher.LaunchUriAsync(uri));
            content.Children.Clear(); content.Children.Add(control); _settings.HomepageFile = file; SaveSettings();
        }), ActionButton("恢复默认主页", _ => { content.Children.Clear(); _settings.HomepageFile = null; SaveSettings(); return Task.CompletedTask; })));
        content.Children.Add(Label("欢迎使用 PCL Community Edition。选择版本后即可启动，也可在此载入自定义主页。"));
        if (_settings.HomepageFile is { } path && File.Exists(path))
        {
            try { content.Children.Clear(); content.Children.Add(CustomHomeRenderer.Render(File.ReadAllText(path), uri => _ = Launcher.LaunchUriAsync(uri))); }
            catch (Exception ex) { content.Children.Add(Label("主页无法载入：" + ex.Message)); }
        }
        panel.Children.Add(content); return new ScrollViewer { Content = panel };
    }
    private Control NewsPage()
    {
        var panel = Page(); var news = new ListBox { Height = 330 };
        news.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<PCL.Core.Model.Tools.News.NewsItem>((item, _) =>
            new TextBlock { Text = item?.Title + "\n" + item?.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
        panel.Children.Add(Heading("Minecraft 新闻")); var page = 0;
        panel.Children.Add(ActionButton("载入更多新闻", async token =>
        {
            var url = "https://net-secondary.web.minecraft-services.net/api/v1.0/zh-cn/search?pageSize=24&sortType=Recent&category=News&newsOnly=true&page=" + (++page);
            var json = await _http.GetStringAsync(url, token);
            var response = System.Text.Json.JsonSerializer.Deserialize<PCL.Core.Model.Tools.News.ApiResponse>(json);
            var items = news.ItemsSource?.Cast<PCL.Core.Model.Tools.News.NewsItem>().ToList() ?? [];
            foreach (var item in response?.Result.Results ?? []) { item.Description = System.Net.WebUtility.HtmlDecode(item.Description); items.Add(item); }
            news.ItemsSource = items;
        })); panel.Children.Add(news);
        panel.Children.Add(ActionButton("阅读所选新闻", async _ =>
        {
            var item = news.SelectedItem as PCL.Core.Model.Tools.News.NewsItem ?? throw new InvalidOperationException("请选择新闻。");
            if (!PCL.Core.ViewModel.Homepage.NewsViewModel.IsSafeNewsLink(item.Url)) throw new InvalidDataException("新闻链接不属于可信来源。");
            await Launcher.LaunchUriAsync(new Uri(item.Url));
        })); return new ScrollViewer { Content = panel };
    }

}
