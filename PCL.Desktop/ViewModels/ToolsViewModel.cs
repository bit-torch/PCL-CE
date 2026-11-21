using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
using PCL.Kernel.GameData;
using PCL.Kernel.Install;
using PCL.Kernel.Modpacks;
using PCL.Kernel.Updates;

namespace PCL.Desktop.ViewModels;

/// <summary>存档、服务器、截图、日志分析与整合包导入的本地工具集合。</summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;

    public ToolsViewModel(LauncherServices services)
    {
        _services = services;
        _strings = services.Localization;

        status = _strings["tools.status.idle"];
        importStatus = _strings["tools.modpack.hint"];
        updateStatus = _strings["tools.update.idle"];

        Refresh();
    }

    public ObservableCollection<SaveEntry> Saves { get; } = [];

    public ObservableCollection<ServerEntry> Servers { get; } = [];

    public ObservableCollection<ScreenshotEntry> Screenshots { get; } = [];

    public ObservableCollection<LogIssue> LogIssues { get; } = [];

    public bool HasSaves => Saves.Count > 0;

    public bool HasServers => Servers.Count > 0;

    public bool HasScreenshots => Screenshots.Count > 0;

    public bool HasLogIssues => LogIssues.Count > 0;

    public string GameDirectory => _services.Paths.GameDirectory;

    public string ImportDialogTitle => _strings["tools.modpack.dialog"];

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
    private string updateStatus;

    private string? _updateUrl;

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var gameDirectory = _services.Paths.GameDirectory;

            Saves.Clear();
            foreach (var save in SaveScanner.Scan(gameDirectory))
                Saves.Add(save);

            Servers.Clear();
            foreach (var server in ServerList.Read(gameDirectory))
                Servers.Add(server);

            Screenshots.Clear();
            foreach (var screenshot in ScreenshotScanner.Scan(gameDirectory))
                Screenshots.Add(screenshot);

            LogIssues.Clear();
            var analysis = LogAnalyzer.Analyze(gameDirectory);
            foreach (var issue in analysis.Issues)
                LogIssues.Add(issue);

            OnPropertyChanged(nameof(HasSaves));
            OnPropertyChanged(nameof(HasServers));
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

    public async Task ImportModpackAsync(string archivePath)
    {
        if (IsImporting)
            return;

        IsImporting = true;
        Progress = 0;
        ImportStatus = _strings["tools.modpack.reading"];

        try
        {
            var installer = new MrPackInstaller(_services.Paths.GameDirectory, _services.Downloads);
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

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdate)
            return;

        IsCheckingUpdate = true;
        UpdateStatus = _strings["tools.update.checking"];

        try
        {
            var info = await _services.UpdateChecker.CheckAsync(UpdateChecker.CurrentVersion);
            HasUpdate = info.IsAvailable;
            _updateUrl = info.HtmlUrl ?? info.DownloadUrl;

            UpdateStatus = info.IsAvailable
                ? string.Format(_strings["tools.update.available"],
                    info.LatestVersion, info.CurrentVersion)
                : string.Format(_strings["tools.update.none"], info.CurrentVersion);
        }
        catch (Exception ex)
        {
            HasUpdate = false;
            UpdateStatus = _strings["tools.update.error"] + ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
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
