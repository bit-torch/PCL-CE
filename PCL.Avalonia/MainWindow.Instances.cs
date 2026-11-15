using ResourceProvider = PCL.CrossPlatform.ResourceProvider;
using Avalonia.Controls.Templates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PCL.Core.Link.McPing;
using PCL.Core.Minecraft.Saves;
using PCL.Core.Minecraft.Saves.Editing;
using PCL.CrossPlatform;

namespace PCL.Avalonia;

public sealed partial class MainWindow
{
    private Control InstancePages() => Tabs(("设置", InstanceSetupPage()), ("Mod", LocalFilesPage("mods")), ("资源包", LocalFilesPage("resourcepacks")),
        ("光影与投影", Tabs(("光影", LocalFilesPage("shaderpacks")), ("投影", LocalFilesPage("schematics")))), ("存档", SavesPage()), ("服务器", ServersPage()), ("截图", ScreenshotsPage()));
    private InstancePreferences InstanceOptions => _settings.Instances.TryGetValue(InstanceKey, out var value) ? value : _settings.Instances[InstanceKey] = new();
    private string InstanceKey => GameRoot + "|" + SelectedVersion;
    private string CurrentDataDirectory => InstanceFiles.DataDirectory(GameRoot, SelectedVersion, InstanceOptions.Isolated);
    private string TrashDirectory => Path.Combine(GameRoot, ".pcl-trash");

    private Control InstanceSetupPage()
    {
        var panel = Page(); panel.Children.Add(Heading("当前实例设置"));
        var isolated = new CheckBox { Content = "版本隔离（Mod、存档与游戏设置使用独立目录）" };
        var jvm = new TextBox { Watermark = "附加 JVM 参数" }; var arguments = new TextBox { Watermark = "附加游戏参数" };
        var environment = new TextBox { Watermark = "环境变量，每行 NAME=VALUE", AcceptsReturn = true, Height = 80 };
        var width = new NumericUpDown { Minimum = 320, Maximum = 16384, Value = 854 }; var height = new NumericUpDown { Minimum = 240, Maximum = 16384, Value = 480 };
        var server = new TextBox { Watermark = "启动后加入的服务器地址（可留空）" };
        var java = new TextBox { Watermark = "此版本的 Java 绝对路径（留空使用全局设置）" };
        var memory = new NumericUpDown { Minimum = 0, Maximum = 1048576, Value = 0 };
        panel.Children.Add(ActionButton("载入当前版本设置", _ =>
        {
            var options = InstanceOptions; java.Text = options.JavaPath; memory.Value = options.MemoryMb; isolated.IsChecked = options.Isolated; jvm.Text = options.JvmArguments;
            arguments.Text = options.GameArguments; environment.Text = options.EnvironmentVariables;
            width.Value = options.Width; height.Value = options.Height; server.Text = options.ServerAddress;
            Report("已载入 " + SelectedVersion + " 的设置。"); return Task.CompletedTask;
        }));
        panel.Children.Add(java); panel.Children.Add(Label("此版本最大内存（MiB，0 使用全局设置）")); panel.Children.Add(memory);
        panel.Children.Add(isolated); panel.Children.Add(jvm); panel.Children.Add(arguments); panel.Children.Add(environment);
        panel.Children.Add(Label("游戏窗口宽度与高度")); panel.Children.Add(Row(width, height)); panel.Children.Add(server);
        panel.Children.Add(ActionButton("保存当前版本设置", _ =>
        {
            var options = InstanceOptions; options.JavaPath = java.Text; options.MemoryMb = (int)(memory.Value ?? 0); options.Isolated = isolated.IsChecked == true; options.JvmArguments = jvm.Text ?? "";
            options.GameArguments = arguments.Text ?? ""; options.EnvironmentVariables = environment.Text ?? "";
            options.Width = (int)(width.Value ?? 854); options.Height = (int)(height.Value ?? 480); options.ServerAddress = server.Text ?? "";
            SaveSettings(); Report("实例设置已保存。"); return Task.CompletedTask;
        }));
        var copyName = new TextBox { Watermark = "副本版本名称" }; panel.Children.Add(copyName);
        panel.Children.Add(Row(ActionButton("创建版本副本", async token =>
        {
            EnsureGameStopped(); GameManifest.ValidateId(copyName.Text ?? "");
            var path = GameManifest.VersionPath(GameRoot, copyName.Text!);
            if (File.Exists(path)) throw new IOException("同名版本已存在。");
            var version = new System.Text.Json.Nodes.JsonObject { ["id"] = copyName.Text, ["inheritsFrom"] = SelectedVersion };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, version.ToJsonString(), token);
            SelectInstalled(copyName.Text!); InstanceOptions.Isolated = true; SaveSettings(); Report("版本副本已创建，共享游戏依赖并使用独立数据目录。");
        }), ActionButton("移除当前版本（保留备份）", _ =>
        {
            EnsureGameStopped(); var path = Path.GetDirectoryName(GameManifest.VersionPath(GameRoot, SelectedVersion))!;
            foreach (var candidate in Directory.GetFiles(Path.Combine(GameRoot, "versions"), "*.json", SearchOption.AllDirectories))
                if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(candidate))?["inheritsFrom"]?.ToString() == SelectedVersion)
                    throw new InvalidOperationException("其他版本依赖此版本，请先移除其派生版本。");
            Report("版本已移至备份：" + InstanceFiles.Archive(path, TrashDirectory)); RefreshInstalled(); return Task.CompletedTask;
        })));
        panel.Children.Add(ActionButton("打开备份目录", async _ => { Directory.CreateDirectory(TrashDirectory); await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(TrashDirectory)); }));
        return new ScrollViewer { Content = panel };
    }

    private Control LocalFilesPage(string folder, Func<string>? root = null)
    {
        var panel = Page(); var files = new ListBox { Height = 250 }; var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130 };
        var game = new TextBox { Watermark = "检查更新时使用的 Minecraft 版本" }; var loader = new TextBox { Watermark = "加载器，例如 fabric" };
        string DirectoryPath() => Path.Combine(root?.Invoke() ?? CurrentDataDirectory, folder);
        void Refresh() => files.ItemsSource = InstanceFiles.List(DirectoryPath());
        InstanceFile Selected() => files.SelectedItem as InstanceFile ?? throw new InvalidOperationException("请选择文件。");
        panel.Children.Add(Heading(folder));
        panel.Children.Add(Row(ActionButton("刷新", _ => { Refresh(); return Task.CompletedTask; }), ActionButton("导入文件", async token =>
        {
            EnsureGameStopped(); var file = await PickFile("选择资源文件"); if (file is null) return;
            await InstanceFiles.ImportFileAsync(file, DirectoryPath(), token); Refresh();
        }), ActionButton("打开目录", async _ => { Directory.CreateDirectory(DirectoryPath()); await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(DirectoryPath())); })));
        panel.Children.Add(files);
        panel.Children.Add(Row(ActionButton("启用 / 禁用", _ => { EnsureGameStopped(); InstanceFiles.Toggle(Selected().Path); Refresh(); return Task.CompletedTask; }),
            ActionButton("移至备份", _ => { EnsureGameStopped(); Report("文件已移至：" + InstanceFiles.Archive(Selected().Path, TrashDirectory)); Refresh(); return Task.CompletedTask; }),
            ActionButton("读取详情与内嵌 Mod", async token =>
            {
                var path = Selected().Path;
                details.Text = await Task.Run(() =>
                {
                    var info = new LocalComp.LocalCompFile(path); info.LoadBasicInfo();
                    return $"{info.Name}\n{info.Version}\n{info.Description}\n作者：{info.Authors}\n依赖：{string.Join(", ", info.Dependencies.Keys)}\n内嵌：{string.Join(", ", info.EmbeddedMods.Select(m => m.Name))}";
                }, token);
            })));
        panel.Children.Add(details); panel.Children.Add(game); panel.Children.Add(loader);
        panel.Children.Add(ActionButton("检查并安装选中文件的更新", async token =>
        {
            EnsureGameStopped(); var file = Selected(); var catalog = new ResourceCatalog(_http);
            var update = await catalog.FindUpdateAsync(file.Path, game.Text ?? "", loader.Text ?? "", token);
            if (update is null) { Report("没有找到匹配的更新。"); return; }
            var stage = Path.Combine(PCL.Core.App.Paths.Temp, "resource-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                var downloads = await catalog.InstallAsync(ResourceProvider.Modrinth, update, stage, game.Text ?? "", loader.Text ?? "", token);
                foreach (var item in downloads)
                {
                    var target = Path.Combine(DirectoryPath(), Path.GetFileName(item));
                    if (File.Exists(target)) InstanceFiles.Archive(target, TrashDirectory);
                    File.Move(item, target);
                }
                if (File.Exists(file.Path) && !downloads.Any(d => Path.GetFileName(d) == file.Name)) InstanceFiles.Archive(file.Path, TrashDirectory);
                Refresh(); Report("资源已更新，旧文件已备份。");
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }));
        return new ScrollViewer { Content = panel };
    }

    private Control SavesPage()
    {
        var panel = Page(); var saves = new ListBox { Height = 230 }; var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var difficulty = new ComboBox { ItemsSource = Enum.GetValues<Difficulty>(), SelectedIndex = 2 };
        var commands = new CheckBox { Content = "允许作弊命令" }; var locked = new CheckBox { Content = "锁定难度" };
        SaveInfo Selected() => saves.SelectedItem as SaveInfo ?? throw new InvalidOperationException("请选择存档。");
        panel.Children.Add(Heading("存档管理"));
        async Task Refresh(CancellationToken token)
        {
            var list = new List<SaveInfo>(); await foreach (var save in new SaveManager().LoadSavesAsync(Path.Combine(CurrentDataDirectory, "saves"), token)) list.Add(save);
            saves.ItemsSource = list; saves.ItemTemplate = new FuncDataTemplate<SaveInfo>((save, _) => new TextBlock { Text = $"{save?.LevelName} · {save?.VersionName} · {save?.GameMode}" });
        }
        saves.SelectionChanged += (_, _) => { if (saves.SelectedItem is SaveInfo save) { summary.Text = $"种子：{save.Seed} · 时间：{save.PlayTime}\n{save.FolderPath}"; commands.IsChecked = save.AllowCommands; locked.IsChecked = save.IsDifficultyLocked; difficulty.SelectedItem = save.Difficulty ?? Difficulty.Normal; } };
        panel.Children.Add(ActionButton("读取存档", Refresh)); panel.Children.Add(saves); panel.Children.Add(summary);
        panel.Children.Add(ActionButton("管理所选存档的数据包", async _ =>
        {
            var save = Selected();
            var dialog = new Window { Title = "数据包 · " + save.LevelName, Width = 780, Height = 650,
                Content = LocalFilesPage("datapacks", () => save.FolderPath) };
            await dialog.ShowDialog(this);
        }));
        panel.Children.Add(Row(difficulty, commands, locked));
        panel.Children.Add(Row(ActionButton("保存存档设置", async token =>
        {
            EnsureGameStopped(); var save = Selected();
            var backup = Path.Combine(TrashDirectory, "save-" + Guid.NewGuid().ToString("N") + ".zip"); Directory.CreateDirectory(TrashDirectory);
            await InstanceFiles.ExportAsync(save.FolderPath, backup, token);
            await new SaveManager().ApplyChangesAsync(save.FolderPath, new SaveChanges { AllowCommands = new(commands.IsChecked == true), LockDifficulty = new(locked.IsChecked == true), Difficulty = new((Difficulty)difficulty.SelectedItem!) }, token);
            await Refresh(token); Report("存档设置已保存，原存档已备份。");
        }), ActionButton("导出存档", async token => { var target = await PickSave("导出存档", "world.zip"); if (target is not null) await InstanceFiles.ExportAsync(Selected().FolderPath, target, token); }),
            ActionButton("移至备份", async token => { EnsureGameStopped(); InstanceFiles.Archive(Selected().FolderPath, TrashDirectory); await Refresh(token); })));
        panel.Children.Add(ActionButton("导入存档压缩包", async token =>
        {
            EnsureGameStopped(); var source = await PickFile("选择存档压缩包"); if (source is null) return;
            var stage = Path.Combine(PCL.Core.App.Paths.Temp, "save-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            try
            {
                await InstanceFiles.ExtractAsync(source, stage, token: token);
                var root = PCL.Core.Minecraft.SaveImportHelper.GetSaveRootDirectory(stage) ?? throw new InvalidDataException("没有找到 level.dat。");
                var name = Path.GetFileNameWithoutExtension(source); GameManifest.ValidateId(name);
                var destination = GameManifest.SafePath(Path.Combine(CurrentDataDirectory, "saves"), name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Directory.Move(root, destination); await Refresh(token);
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }));
        return new ScrollViewer { Content = panel };
    }

    private Control ServersPage()
    {
        var panel = Page(); var list = new ListBox { Height = 200 }; var name = new TextBox { Watermark = "服务器名称" }; var address = new TextBox { Watermark = "地址，支持 IPv6 与端口" };
        string FilePath() => Path.Combine(CurrentDataDirectory, "servers.dat");
        void Refresh() => list.ItemsSource = ServerStore.Read(FilePath());
        panel.Children.Add(Heading("服务器收藏")); panel.Children.Add(ActionButton("读取服务器列表", _ => { Refresh(); return Task.CompletedTask; })); panel.Children.Add(list);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is SavedServer item) { name.Text = item.Name; address.Text = item.Address; } };
        panel.Children.Add(name); panel.Children.Add(address);
        panel.Children.Add(Row(ActionButton("保存服务器", _ =>
        {
            EnsureGameStopped(); if (string.IsNullOrWhiteSpace(address.Text)) throw new InvalidOperationException("请填写地址。");
            var servers = ServerStore.Read(FilePath()).ToList(); if (list.SelectedItem is SavedServer old) servers.Remove(old);
            servers.Add(new(name.Text ?? address.Text, address.Text)); ServerStore.Write(FilePath(), servers); Refresh(); return Task.CompletedTask;
        }), ActionButton("移除收藏", _ => { EnsureGameStopped(); var servers = ServerStore.Read(FilePath()).ToList(); if (list.SelectedItem is SavedServer old) servers.Remove(old); ServerStore.Write(FilePath(), servers); Refresh(); return Task.CompletedTask; }),
            ActionButton("检查服务器状态", async token =>
            {
                var uri = new Uri("minecraft://" + address.Text); var port = uri.Port < 0 ? 25565 : uri.Port;
                var result = await McPingServiceFactory.CreateService(uri.Host, port).PingAsync(token); Report(System.Text.Json.JsonSerializer.Serialize(result));
            })));
        panel.Children.Add(ActionButton("启动并加入此服务器", async token => { InstanceOptions.ServerAddress = address.Text ?? ""; SaveSettings(); await Launch(token); }));
        return new ScrollViewer { Content = panel };
    }

    private Control ScreenshotsPage()
    {
        var panel = Page(); var list = new ListBox { Height = 160 }; var image = new Image { Height = 260, Stretch = Stretch.Uniform }; Bitmap? bitmap = null;
        panel.Children.Add(Heading("游戏截图"));
        panel.Children.Add(ActionButton("刷新截图", _ => { list.ItemsSource = InstanceFiles.List(Path.Combine(CurrentDataDirectory, "screenshots")); return Task.CompletedTask; })); panel.Children.Add(list);
        list.SelectionChanged += (_, _) => { bitmap?.Dispose(); bitmap = null; if (list.SelectedItem is InstanceFile file) { bitmap = new Bitmap(file.Path); image.Source = bitmap; } };
        panel.Children.Add(image); panel.Children.Add(Row(ActionButton("打开截图", async _ => { if (list.SelectedItem is InstanceFile file) await Launcher.LaunchFileInfoAsync(new FileInfo(file.Path)); }),
            ActionButton("移至备份", _ => { if (list.SelectedItem is InstanceFile file) { image.Source = null; bitmap?.Dispose(); bitmap = null; InstanceFiles.Archive(file.Path, TrashDirectory); list.ItemsSource = InstanceFiles.List(Path.Combine(CurrentDataDirectory, "screenshots")); } return Task.CompletedTask; })));
        Closed += (_, _) => bitmap?.Dispose(); return new ScrollViewer { Content = panel };
    }
}
