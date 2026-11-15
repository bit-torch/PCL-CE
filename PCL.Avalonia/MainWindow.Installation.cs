using ResourceProvider = PCL.CrossPlatform.ResourceProvider;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PCL.Core.Utils.Secret;
using PCL.CrossPlatform;

namespace PCL.Avalonia;

public sealed partial class MainWindow
{
    private Control InstallationPages() => Tabs(("Minecraft", DownloadPage()), ("加载器", LoaderPage()), ("资源搜索", ResourcesPage()), ("整合包", ModpackPage()), ("Java", JavaDownloadPage()));
    private static TabControl Tabs(params (string Name, Control Page)[] pages)
    {
        var tabs = new TabControl(); foreach (var (name, page) in pages) tabs.Items.Add(new TabItem { Header = name, Content = page }); return tabs;
    }
    private Control LoaderPage()
    {
        var panel = Page(); panel.Children.Add(Heading("安装加载器"));
        var minecraft = new TextBox { Watermark = "Minecraft 版本，例如 1.20.1" };
        var kind = new ComboBox { ItemsSource = Enum.GetValues<LoaderKind>(), SelectedIndex = 0 };
        var versions = new ListBox { Height = 230 };
        panel.Children.Add(minecraft); panel.Children.Add(kind);
        panel.Children.Add(ActionButton("获取加载器版本", async token =>
        {
            versions.ItemsSource = await new LoaderInstaller(_http).GetVersionsAsync((LoaderKind)kind.SelectedItem!, minecraft.Text?.Trim() ?? "", token); versions.SelectedIndex = 0;
        })); panel.Children.Add(versions);
        panel.Children.Add(Row(ActionButton("安装加载器", async token =>
        {
            if (versions.SelectedItem is not LoaderVersion version) throw new InvalidOperationException("请选择加载器版本。");
            var id = await new LoaderInstaller(_http).InstallAsync(GameRoot, _java.Text!, version, Progress(), token);
            SelectInstalled(id); Report("加载器安装完成：" + id);
        }), ActionButton("从本地安装器安装", async token =>
        {
            var file = await PickFile("选择加载器安装器"); if (file is null) return;
            var id = await new LoaderInstaller(_http).InstallLocalAsync(GameRoot, _java.Text!, file, minecraft.Text?.Trim() ?? "", Progress(), token);
            SelectInstalled(id); Report("加载器安装完成：" + id);
        })));
        return new ScrollViewer { Content = panel };
    }
    private Control ResourcesPage()
    {
        var panel = Page(); var provider = new ComboBox { ItemsSource = Enum.GetValues<ResourceProvider>(), SelectedIndex = 0 };
        var kind = new ComboBox { ItemsSource = Enum.GetValues<ResourceKind>(), SelectedIndex = 0 };
        var query = new TextBox { Watermark = "搜索名称" }; var game = new TextBox { Watermark = "Minecraft 版本筛选（可留空）" };
        var loader = new TextBox { Watermark = "加载器筛选，例如 fabric、forge（可留空）" };
        var projects = new ListBox { Height = 180 }; var releases = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(Heading("资源下载")); panel.Children.Add(Row(provider, kind)); panel.Children.Add(query); panel.Children.Add(game); panel.Children.Add(loader);
        ResourceCatalog Catalog() => new(_http, CurseForgeKey);
        panel.Children.Add(Row(ActionButton("搜索资源", async token => { projects.ItemsSource = await Catalog().SearchAsync((ResourceProvider)provider.SelectedItem!, (ResourceKind)kind.SelectedItem!, query.Text ?? "", game.Text ?? "", loader.Text ?? "", token); projects.SelectedIndex = 0; }),
            ActionButton("显示收藏", _ => { projects.ItemsSource = _settings.Favorites.Select(p => new ResourceProject(p.Provider, p.Id, p.Title, "", p.Url)).ToArray(); return Task.CompletedTask; })));
        panel.Children.Add(projects);
        panel.Children.Add(Row(ActionButton("获取可用版本", async token =>
        {
            var project = projects.SelectedItem as ResourceProject ?? throw new InvalidOperationException("请选择项目。");
            releases.ItemsSource = await Catalog().VersionsAsync(project, game.Text ?? "", loader.Text ?? "", token); releases.SelectedIndex = 0;
        }), ActionButton("收藏 / 取消收藏", _ =>
        {
            var project = projects.SelectedItem as ResourceProject ?? throw new InvalidOperationException("请选择项目。");
            var index = _settings.Favorites.FindIndex(p => p.Provider == project.Provider && p.Id == project.Id);
            if (index >= 0) _settings.Favorites.RemoveAt(index); else _settings.Favorites.Add(new(project.Provider, project.Id, project.Title, project.WebUrl));
            SaveSettings(); Report("收藏已更新。"); return Task.CompletedTask;
        }), ActionButton("项目网页", async _ => { if (projects.SelectedItem is ResourceProject project) await Launcher.LaunchUriAsync(new Uri(project.WebUrl)); })));
        panel.Children.Add(releases);
        panel.Children.Add(ActionButton("下载并安装所选资源", async token =>
        {
            var project = projects.SelectedItem as ResourceProject ?? throw new InvalidOperationException("请选择项目。");
            var release = releases.SelectedItem as ResourceRelease ?? throw new InvalidOperationException("请选择文件版本。");
            var resourceKind = (ResourceKind)kind.SelectedItem!;
            var destination = resourceKind == ResourceKind.Modpack ? Path.Combine(PCL.Core.App.Paths.Temp, "modpacks")
                : Path.Combine(CurrentDataDirectory, resourceKind switch { ResourceKind.Mod => "mods", ResourceKind.Shader => "shaderpacks", _ => "resourcepacks" });
            EnsureGameStopped();
            var files = await Catalog().InstallAsync(project.Provider, release, destination, game.Text ?? "", loader.Text ?? "", token);
            Report("已安装：" + string.Join(", ", files.Select(Path.GetFileName)));
            if (resourceKind == ResourceKind.Modpack) Report("整合包已下载到：" + files.Last() + "。请在整合包页导入。");
        }));
        return new ScrollViewer { Content = panel };
    }
    private Control ModpackPage()
    {
        var panel = Page(); panel.Children.Add(Heading("整合包导入与导出"));
        var name = new TextBox { Watermark = "新实例名称" };
        var optional = new CheckBox { Content = "安装可选文件" };
        panel.Children.Add(name); panel.Children.Add(optional);
        panel.Children.Add(ActionButton("导入整合包", async token =>
        {
            var file = await PickFile("选择整合包（mrpack / zip）"); if (file is null) return;
            GameManifest.ValidateId(name.Text ?? "");
            var result = await new ModpackInstaller(_http, CurseForgeKey).InstallAsync(file,
                GameManifest.SafePath(GameRoot, "instances/" + name.Text), _java.Text!, optional.IsChecked == true, Progress(), token);
            _root.Text = result.Root; _settings.GameDirectory = result.Root; SelectInstalled(result.VersionId); SaveSettings();
            Report("整合包安装完成。");
        }));
        panel.Children.Add(ActionButton("导出当前实例", async token =>
        {
            EnsureGameStopped(); var target = await PickSave("导出实例", "instance.zip"); if (target is null) return;
            await InstanceFiles.ExportAsync(CurrentDataDirectory, target, token); Report("实例已导出：" + target);
        }));
        panel.Children.Add(Label("导入在独立暂存目录完成；失败或取消不会留下半成品实例。导出排除启动器凭据及密钥。"));
        return new ScrollViewer { Content = panel };
    }
    private string? CurseForgeKey => string.IsNullOrWhiteSpace(_settings.EncryptedCurseForgeKey) ? null : EncryptHelper.SecretDecrypt(_settings.EncryptedCurseForgeKey);
    private async Task<string?> PickFile(string title)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> PickSave(string title, string name)
    {
        var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = name, ShowOverwritePrompt = true });
        return result?.TryGetLocalPath();
    }
    private void SelectInstalled(string id) { _settings.SelectedVersion = id; RefreshInstalled(); _installed.SelectedItem = id; }
    private void EnsureGameStopped() { if (_game is { HasExited: false }) throw new InvalidOperationException("请先退出游戏。"); }
}
