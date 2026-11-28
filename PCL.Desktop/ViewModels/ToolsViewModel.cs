using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
using PCL.Kernel.GameData;
using PCL.Kernel.Install;
using PCL.Kernel.Modpacks;
using PCL.Kernel.Net;
using PCL.Kernel.Updates;

namespace PCL.Desktop.ViewModels;

/// <summary>存档、服务器、截图、日志分析与整合包导入的本地工具集合。</summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;

    private UpdateInfo? _latestUpdate;
    private PreparedUpdate? _preparedUpdate;

    public ToolsViewModel(LauncherServices services)
    {
        _services = services;
        _strings = services.Localization;

        status = _strings["tools.status.idle"];
        importStatus = _strings["tools.modpack.hint"];
        updateStatus = _strings["tools.update.idle"];

        LoadTargets();
        Refresh();
        _ = RefreshLogsAsync();
    }

    public ObservableCollection<SaveEntry> Saves { get; } = [];

    public ObservableCollection<ServerEntry> Servers { get; } = [];

    public ObservableCollection<ModEntry> Mods { get; } = [];

    public ObservableCollection<NestedMod> NestedMods { get; } = [];

    public ObservableCollection<ResourcePackEntry> ResourcePacks { get; } = [];

    public ObservableCollection<ScreenshotEntry> Screenshots { get; } = [];

    public ObservableCollection<LogIssue> LogIssues { get; } = [];

    /// <summary>全局游戏目录或启用隔离的实例目录。</summary>
    public ObservableCollection<InstanceTarget> Targets { get; } = [];

    public ObservableCollection<LogFileEntry> LogFiles { get; } = [];

    public bool HasSaves => Saves.Count > 0;

    public bool HasServers => Servers.Count > 0;

    public bool HasMods => Mods.Count > 0;

    public bool HasResourcePacks => ResourcePacks.Count > 0;

    public bool HasScreenshots => Screenshots.Count > 0;

    public bool HasLogIssues => LogIssues.Count > 0;

    public bool HasLogFiles => LogFiles.Count > 0;

    public bool HasSelectedLog => SelectedLog is not null;

    public string LogsDirectory => Path.Combine(GameDirectory, "logs");

    public bool HasSelectedSave => SelectedSave is not null;

    public bool HasSelectedServer => SelectedServer is not null;

    public bool HasPingStatus => PingStatus is not null;

    public string PingSummary => PingStatus switch
    {
        null => "",
        { IsReachable: true } status => string.Format(
            _strings["tools.servers.online"],
            status.VersionName,
            status.OnlinePlayers,
            status.MaxPlayers,
            status.LatencyMs,
            status.Motd),
        { } status => string.Format(
            _strings["tools.servers.offline"],
            status.Error ?? "")
    };

    public bool HasSelectedMod => SelectedMod is not null;

    public bool HasNestedMods => NestedMods.Count > 0;

    public bool HasSelectedResourcePack => SelectedResourcePack is not null;

    /// <summary>当前操作目标目录：全局游戏目录或所选隔离实例目录。</summary>
    public string GameDirectory =>
        SelectedTarget?.GameDirectory ?? _services.Paths.GameDirectory;

    public string ImportDialogTitle => _strings["tools.modpack.dialog"];

    public string ExportDialogTitle => _strings["tools.logs.dialog"];

    public string ExportDefaultName =>
        $"PCL-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    public string ImportSaveDialogTitle => _strings["tools.saves.dialogImport"];

    public string ExportSaveDialogTitle => _strings["tools.saves.dialogExport"];

    public string ExportSaveDefaultName =>
        (SelectedSave?.Name ?? "world") + ".zip";

    [ObservableProperty]
    private string status;

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string importStatus;

    [ObservableProperty]
    private bool hasUpdate;

    [ObservableProperty]
    private bool isCheckingUpdate;

    [ObservableProperty]
    private bool isDownloadingUpdate;

    [ObservableProperty]
    private double updateProgress;

    [ObservableProperty]
    private bool hasPreparedUpdate;

    [ObservableProperty]
    private string updateStatus;

    [ObservableProperty]
    private SaveEntry? selectedSave;

    [ObservableProperty]
    private ServerEntry? selectedServer;

    [ObservableProperty]
    private ModEntry? selectedMod;

    [ObservableProperty]
    private ResourcePackEntry? selectedResourcePack;

    [ObservableProperty]
    private InstanceTarget? selectedTarget;

    [ObservableProperty]
    private LogFileEntry? selectedLog;

    [ObservableProperty]
    private string logContent = "";

    [ObservableProperty]
    private string logFilter = "";

    private bool _targetsReady;

    [ObservableProperty]
    private string newServerName = "";

    [ObservableProperty]
    private string newServerIp = "";

    partial void OnSelectedSaveChanged(SaveEntry? value)
    {
        OnPropertyChanged(nameof(HasSelectedSave));
        OnPropertyChanged(nameof(ExportSaveDefaultName));
    }

    partial void OnSelectedServerChanged(ServerEntry? value)
    {
        OnPropertyChanged(nameof(HasSelectedServer));
        PingStatus = null;
    }

    [ObservableProperty]
    private ServerStatus? pingStatus;

    [ObservableProperty]
    private bool isPingingServer;

    partial void OnPingStatusChanged(ServerStatus? value)
    {
        OnPropertyChanged(nameof(HasPingStatus));
        OnPropertyChanged(nameof(PingSummary));
    }

    partial void OnSelectedModChanged(ModEntry? value)
    {
        OnPropertyChanged(nameof(HasSelectedMod));
        NestedMods.Clear();
        OnPropertyChanged(nameof(HasNestedMods));
    }

    partial void OnSelectedResourcePackChanged(ResourcePackEntry? value) =>
        OnPropertyChanged(nameof(HasSelectedResourcePack));

    partial void OnSelectedTargetChanged(InstanceTarget? value)
    {
        OnPropertyChanged(nameof(GameDirectory));
        OnPropertyChanged(nameof(LogsDirectory));

        if (!_targetsReady)
            return;

        Refresh();
        _ = RefreshLogsAsync();
    }

    partial void OnSelectedLogChanged(LogFileEntry? value)
    {
        OnPropertyChanged(nameof(HasSelectedLog));
        if (value is not null)
            _ = LoadLogAsync();
    }

    private void LoadTargets()
    {
        Targets.Clear();
        foreach (var target in InstanceTargets.Build(_services, _strings))
            Targets.Add(target);

        SelectedTarget = Targets[0];
        _targetsReady = true;
    }

    private string? _updateUrl;

    public bool CanDownloadUpdate =>
        HasUpdate && _latestUpdate is not null &&
        UpdateInstaller.IsSupportedArchive(_latestUpdate.DownloadUrl ?? "");

    partial void OnHasUpdateChanged(bool value) =>
        OnPropertyChanged(nameof(CanDownloadUpdate));

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var gameDirectory = GameDirectory;

            SelectedSave = null;
            SelectedServer = null;
            SelectedMod = null;
            SelectedResourcePack = null;

            Saves.Clear();
            foreach (var save in SaveScanner.Scan(gameDirectory))
                Saves.Add(save);

            Servers.Clear();
            foreach (var server in ServerList.Read(gameDirectory))
                Servers.Add(server);

            Mods.Clear();
            foreach (var mod in ModScanner.Scan(gameDirectory))
                Mods.Add(mod);

            ResourcePacks.Clear();
            foreach (var pack in ResourcePackManager.Scan(gameDirectory))
                ResourcePacks.Add(pack);

            Screenshots.Clear();
            foreach (var screenshot in ScreenshotScanner.Scan(gameDirectory))
                Screenshots.Add(screenshot);

            LogIssues.Clear();
            var analysis = LogAnalyzer.Analyze(gameDirectory);
            foreach (var issue in analysis.Issues)
                LogIssues.Add(issue);

            OnPropertyChanged(nameof(HasSaves));
            OnPropertyChanged(nameof(HasServers));
            OnPropertyChanged(nameof(HasMods));
            OnPropertyChanged(nameof(HasResourcePacks));
            OnPropertyChanged(nameof(HasScreenshots));
            OnPropertyChanged(nameof(HasLogIssues));
            OnPropertyChanged(nameof(GameDirectory));

            Status = string.Format(
                _strings["tools.status.scanned"],
                Saves.Count, Servers.Count, Screenshots.Count, LogIssues.Count);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        try
        {
            LogFiles.Clear();
            foreach (var entry in LogScanner.Scan([GameDirectory]))
                LogFiles.Add(entry);

            OnPropertyChanged(nameof(HasLogFiles));

            SelectedLog = null;
            SelectedLog = LogFiles.FirstOrDefault(item =>
                              item.Name.Equals("latest.log", StringComparison.OrdinalIgnoreCase))
                          ?? LogFiles.FirstOrDefault();

            if (SelectedLog is null)
            {
                LogContent = "";
                OnPropertyChanged(nameof(HasSelectedLog));
            }
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private Task SearchLogAsync() => LoadLogAsync();

    private async Task LoadLogAsync()
    {
        if (SelectedLog is not { } entry)
            return;

        try
        {
            LogContent = string.IsNullOrWhiteSpace(LogFilter)
                ? await LogReader.ReadTailAsync(entry.Path)
                : await LogReader.SearchAsync(entry.Path, LogFilter);
        }
        catch (Exception ex)
        {
            LogContent = _strings["tools.logs.readError"] + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            Process.Start(new ProcessStartInfo(LogsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void ToggleMod()
    {
        if (SelectedMod is null)
            return;

        var mod = SelectedMod;
        try
        {
            var name = ModScanner.SetEnabled(
                GameDirectory, mod.FileName, !mod.Enabled);
            Refresh();
            Status = string.Format(_strings["tools.status.toggled"], name);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void DeleteMod()
    {
        if (SelectedMod is null)
            return;

        var name = SelectedMod.DisplayName;
        try
        {
            ModScanner.Delete(GameDirectory, SelectedMod.FileName);
            Refresh();
            Status = string.Format(_strings["tools.status.deleted"], name);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void BackupMod()
    {
        if (SelectedMod is null)
            return;

        try
        {
            var path = ModBackupService.Create(GameDirectory, SelectedMod.FileName);
            Status = string.Format(
                _strings["tools.mods.backupDone"],
                Path.GetFileName(Path.GetDirectoryName(path)));
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void RestoreMod()
    {
        if (SelectedMod is not { } mod)
            return;

        try
        {
            var latest = ModBackupService
                .Scan(GameDirectory)
                .FirstOrDefault(backup => string.Equals(
                    StripDisabled(backup.FileName),
                    StripDisabled(mod.DisplayName),
                    StringComparison.OrdinalIgnoreCase));

            if (latest is null)
            {
                Status = _strings["tools.mods.noBackup"];
                return;
            }

            ModBackupService.Restore(GameDirectory, latest);
            Refresh();
            Status = string.Format(_strings["tools.mods.restoreDone"], latest.FileName);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void ScanNestedMods()
    {
        if (SelectedMod is not { } mod)
            return;

        try
        {
            var nested = JarInJarScanner.Scan(mod.FilePath);
            NestedMods.Clear();
            foreach (var item in nested)
                NestedMods.Add(item);

            OnPropertyChanged(nameof(HasNestedMods));
            Status = string.Format(_strings["tools.mods.nestedDone"], nested.Count);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    private const string DisabledSuffix = ".disabled";

    private static string StripDisabled(string fileName) =>
        fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^DisabledSuffix.Length]
            : fileName;

    [RelayCommand]
    private void ToggleResourcePack()
    {
        if (SelectedResourcePack is null)
            return;

        var pack = SelectedResourcePack;
        try
        {
            ResourcePackManager.SetEnabled(
                GameDirectory, pack.Name, !pack.Enabled);
            Refresh();
            Status = string.Format(
                _strings["tools.status.resourcePackToggled"], pack.DisplayName);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void MoveResourcePackUp() => MoveResourcePack(-1);

    [RelayCommand]
    private void MoveResourcePackDown() => MoveResourcePack(1);

    private void MoveResourcePack(int offset)
    {
        if (SelectedResourcePack is null)
            return;

        var pack = SelectedResourcePack;
        try
        {
            if (!ResourcePackManager.Move(GameDirectory, pack.Name, offset))
                return;

            Refresh();
            SelectedResourcePack = ResourcePacks
                .FirstOrDefault(item => item.Name == pack.Name);
            Status = string.Format(
                _strings["tools.status.resourcePackMoved"], pack.DisplayName);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void DeleteSave()
    {
        if (SelectedSave is null)
            return;

        var name = SelectedSave.Name;
        try
        {
            SaveManager.Delete(GameDirectory, name);
            Refresh();
            Status = string.Format(_strings["tools.status.deleted"], name);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task PingServerAsync()
    {
        if (IsPingingServer)
            return;

        if (SelectedServer is null)
        {
            Status = _strings["tools.servers.select"];
            return;
        }

        IsPingingServer = true;
        PingStatus = null;

        try
        {
            PingStatus = await ServerPing.PingAsync(SelectedServer.Ip);
            Status = PingStatus.IsReachable
                ? string.Format(_strings["tools.servers.pingOk"], SelectedServer.DisplayName)
                : string.Format(_strings["tools.servers.pingFailed"], SelectedServer.DisplayName);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
        finally
        {
            IsPingingServer = false;
        }
    }

    [RelayCommand]
    private void DeleteServer()
    {
        if (SelectedServer is null)
            return;

        var name = SelectedServer.DisplayName;
        try
        {
            var list = Servers.ToList();
            list.Remove(SelectedServer);
            ServerList.Save(GameDirectory, list);
            Refresh();
            Status = string.Format(_strings["tools.status.deleted"], name);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        var name = NewServerName.Trim();
        var ip = NewServerIp.Trim();

        if (ip.Length == 0)
        {
            Status = _strings["tools.servers.invalid"];
            return;
        }

        try
        {
            var list = Servers.ToList();
            list.Add(new ServerEntry(name, ip, null));
            ServerList.Save(GameDirectory, list);
            NewServerName = "";
            NewServerIp = "";
            Refresh();
            Status = string.Format(
                _strings["tools.status.serverAdded"],
                string.IsNullOrWhiteSpace(name) ? ip : name);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    public async Task ExportSelectedSaveAsync(string destinationPath)
    {
        if (SelectedSave is null)
            return;

        var save = SelectedSave;
        try
        {
            var gameDirectory = GameDirectory;
            var path = await Task.Run(() => SaveManager.Export(gameDirectory, save.Name, destinationPath));
            Status = string.Format(_strings["tools.status.saveExported"], path);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    public async Task ImportSaveAsync(string archivePath)
    {
        try
        {
            var gameDirectory = GameDirectory;
            var directory = await Task.Run(() => SaveManager.Import(gameDirectory, archivePath));
            Refresh();
            Status = string.Format(
                _strings["tools.status.saveImported"], Path.GetFileName(directory));
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.error"] + ex.Message;
        }
    }

    public async Task ImportModpackAsync(string archivePath)
    {
        if (IsImporting)
            return;

        IsImporting = true;
        Progress = 0;
        ImportStatus = _strings["tools.modpack.reading"];

        try
        {
            var installer = new MrPackInstaller(GameDirectory, _services.Downloads);
            var reporter = new Progress<InstallProgress>(value =>
            {
                Progress = Math.Clamp(value.Fraction * 100, 0, 100);
                ImportStatus = value.Stage;
            });

            var result = await installer.InstallAsync(archivePath, reporter);
            Progress = 100;
            ImportStatus = string.Format(
                _strings["tools.modpack.done"],
                result.Name, result.Files.Count, result.Overrides.Count);

            Refresh();
        }
        catch (Exception ex)
        {
            ImportStatus = _strings["tools.modpack.error"] + ex.Message;
        }
        finally
        {
            IsImporting = false;
        }
    }

    public async Task ExportDiagnosticsAsync(string destinationPath)
    {
        try
        {
            var gameDirectory = GameDirectory;
            var version = UpdateChecker.CurrentVersion;
            var result = await Task.Run(() =>
                DiagnosticExporter.Export(gameDirectory, destinationPath, version));

            Status = string.Format(_strings["tools.status.exported"], result.Path);
        }
        catch (Exception ex)
        {
            Status = _strings["tools.status.exportError"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdate)
            return;

        IsCheckingUpdate = true;
        UpdateStatus = _strings["tools.update.checking"];
        _preparedUpdate = null;
        HasPreparedUpdate = false;
        UpdateProgress = 0;

        try
        {
            var info = await _services.UpdateChecker.CheckAsync(UpdateChecker.CurrentVersion);
            _latestUpdate = info;
            HasUpdate = info.IsAvailable;
            _updateUrl = info.HtmlUrl ?? info.DownloadUrl;

            UpdateStatus = info.IsAvailable
                ? string.Format(_strings["tools.update.available"],
                    info.LatestVersion, info.CurrentVersion)
                : string.Format(_strings["tools.update.none"], info.CurrentVersion);
        }
        catch (Exception ex)
        {
            _latestUpdate = null;
            HasUpdate = false;
            UpdateStatus = _strings["tools.update.error"] + ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (IsDownloadingUpdate || _latestUpdate is null)
            return;

        IsDownloadingUpdate = true;
        UpdateProgress = 0;
        UpdateStatus = _strings["tools.update.downloading"];

        try
        {
            var reporter = new Progress<double>(value =>
                UpdateProgress = Math.Clamp(value * 100, 0, 100));

            var archivePath = await _services.UpdateInstaller.DownloadAsync(_latestUpdate, reporter);
            _preparedUpdate = _services.UpdateInstaller.Prepare(
                archivePath, AppContext.BaseDirectory, _latestUpdate.LatestVersion);
            HasPreparedUpdate = true;
            UpdateProgress = 100;
            UpdateStatus = _strings["tools.update.prepared"];
        }
        catch (Exception ex)
        {
            UpdateStatus = _strings["tools.update.downloadError"] + ex.Message;
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    /// <summary>启动替换脚本。调用方随后应关闭启动器，让脚本完成文件替换与重启。</summary>
    public bool TryStartUpdate()
    {
        if (_preparedUpdate is null)
        {
            UpdateStatus = _strings["tools.update.download"];
            return false;
        }

        try
        {
            var script = UpdateInstaller.CreateApplyScript(
                _preparedUpdate, Environment.ProcessId, Environment.ProcessPath);

            Process.Start(new ProcessStartInfo(script) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            UpdateStatus = _strings["tools.update.applyError"] + ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private async Task OpenUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(_updateUrl))
            return;

        try
        {
            await _services.UrlOpener.OpenAsync(new Uri(_updateUrl));
        }
        catch (Exception ex)
        {
            UpdateStatus = _strings["tools.update.error"] + ex.Message;
        }
    }
}
