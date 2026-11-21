using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
using PCL.Kernel.Accounts;
using PCL.Kernel.Install;
using PCL.Kernel.Java;
using PCL.Kernel.Launch;
using PCL.Kernel.Mojang;
using PCL.Kernel.Versions;

namespace PCL.Desktop.ViewModels;

public partial class LaunchViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;
    private readonly Func<Task<bool>> _licenseGate;
    private GameSession? _session;

    public LaunchViewModel(LauncherServices services, Func<Task<bool>> licenseGate)
    {
        _services = services;
        _strings = services.Localization;
        _licenseGate = licenseGate;
        username = services.Settings.FindAccount(services.Settings.SelectedAccountId)?.Name
                   ?? services.Settings.OfflineUsername;
        memoryMb = services.Settings.MaxMemoryMb;
        javaPath = services.Settings.JavaPath ?? "";
        status = _strings["launch.status.waiting"];
        RefreshList();
    }

    public ObservableCollection<InstalledVersion> Versions { get; } = [];

    [ObservableProperty]
    private InstalledVersion? selectedVersion;

    [ObservableProperty]
    private string username;

    [ObservableProperty]
    private double memoryMb;

    [ObservableProperty]
    private string javaPath;

    [ObservableProperty]
    private string status;

    [ObservableProperty]
    private string log = "";

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool isDeleteConfirmed;

    partial void OnSelectedVersionChanged(InstalledVersion? value) =>
        IsDeleteConfirmed = false;

    public void RefreshList()
    {
        var previous = SelectedVersion?.Id;

        Versions.Clear();
        foreach (var version in InstalledVersionScanner.Scan(_services.Paths))
            Versions.Add(version);

        SelectedVersion = Versions.FirstOrDefault(v => v.Id == previous) ?? Versions.FirstOrDefault();
        IsDeleteConfirmed = false;
        Status = Versions.Count == 0
            ? _strings["launch.status.none"]
            : string.Format(_strings["launch.status.found"], Versions.Count);
    }

    [RelayCommand]
    private void RefreshVersions() => RefreshList();

    [RelayCommand]
    private async Task VerifyVersionAsync()
    {
        if (SelectedVersion is null)
        {
            Status = _strings["launch.status.select"];
            return;
        }

        Status = _strings["launch.status.verifying"];

        try
        {
            var report = await _services.VersionManager.VerifyAsync(SelectedVersion);

            if (report.IsComplete)
            {
                Status = string.Format(_strings["launch.status.verifyOk"], report.CheckedFiles);
                return;
            }

            Status = string.Format(_strings["launch.status.verifyMissing"], report.MissingFiles);
            Log = string.Join(
                Environment.NewLine,
                report.Missing.Take(50).Select(item => _strings["launch.log.missing"] + item));

            if (report.MissingFiles > 50)
                AppendLog(string.Format(_strings["launch.log.moreMissing"], report.MissingFiles - 50));
        }
        catch (Exception ex)
        {
            Status = _strings["launch.error.verify"] + ex.Message;
        }
    }

    [RelayCommand]
    private void DeleteVersion()
    {
        if (SelectedVersion is null)
        {
            Status = _strings["launch.status.select"];
            return;
        }

        if (!IsDeleteConfirmed)
        {
            Status = _strings["launch.status.confirmDelete"];
            return;
        }

        var id = SelectedVersion.Id;

        try
        {
            _services.VersionManager.Delete(SelectedVersion);
            RefreshList();
            Status = string.Format(_strings["launch.status.deleted"], id);
        }
        catch (Exception ex)
        {
            Status = _strings["launch.error.delete"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = _strings["launch.status.detectingJava"];

        try
        {
            var java = await JavaLocator.FindAsync();
            if (java is null)
            {
                Status = _strings["launch.status.javaMissing"];
                return;
            }

            JavaPath = java.Path;
            Status = string.Format(_strings["launch.status.javaFound"], java.DisplayName);
        }
        catch (Exception ex)
        {
            Status = _strings["launch.error.java"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstallJavaAsync()
    {
        if (SelectedVersion is null)
        {
            Status = _strings["launch.status.select"];
            return;
        }

        try
        {
            Status = _strings["launch.status.preparingJava"];
            JavaPath = await InstallJavaRuntimeAsync();

            _services.Settings.JavaPath = JavaPath;
            _services.SaveSettings();
            Status = _strings["launch.status.javaReady"];
        }
        catch (Exception ex)
        {
            Status = _strings["launch.error.javaInstall"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsRunning)
            return;

        if (SelectedVersion is null)
        {
            Status = _strings["launch.status.needVersion"];
            return;
        }

        if (string.IsNullOrWhiteSpace(JavaPath) || !File.Exists(JavaPath))
        {
            await DetectJavaAsync();
            if (string.IsNullOrWhiteSpace(JavaPath) || !File.Exists(JavaPath))
            {
                Status = _strings["launch.status.installingJava"];
                try
                {
                    JavaPath = await InstallJavaRuntimeAsync();
                }
                catch (Exception ex)
                {
                    Status = _strings["launch.error.javaInstall"] + ex.Message;
                    return;
                }
            }
        }

        // 在 PCL 代码与资源完全替换前，保留 PCL 许可要求的正版购买劝导。
        if (!_services.Settings.LicensePrecheckAccepted)
        {
            var accepted = await _licenseGate();
            if (!accepted)
            {
                Status = _strings["launch.status.cancelled"];
                return;
            }

            _services.Settings.LicensePrecheckAccepted = true;
            _services.SaveSettings();
        }

        IsRunning = true;
        Log = "";
        Status = _strings["launch.status.starting"];

        try
        {
            var memory = Math.Clamp((int)MemoryMb, 512, 65536);
            var account = await ResolveAccountAsync();

            if (account is null || account.Kind == AccountKind.Offline)
                _services.Settings.OfflineUsername = Username;

            string? authlibPath = null;
            if (account?.Kind == AccountKind.Yggdrasil)
                authlibPath = await _services.AuthlibInjector.EnsureAsync();

            _services.Settings.MaxMemoryMb = memory;
            _services.Settings.JavaPath = JavaPath;
            _services.Settings.LaunchCount++;
            _services.SaveSettings();

            var gameDirectory = _services.VersionManager.ResolveGameDirectory(
                SelectedVersion.Id, _services.Settings.IsolateVersions);
            Directory.CreateDirectory(gameDirectory);

            _session?.Dispose();
            var session = await _services.Launcher.LaunchAsync(new LaunchOptions
            {
                VersionId = SelectedVersion.Id,
                JavaPath = JavaPath,
                Username = account?.Name ?? Username,
                Uuid = account?.Id,
                AccessToken = account?.AccessToken,
                UserType = account?.Kind == AccountKind.Microsoft ? "msa" : "legacy",
                YggdrasilApiRoot = account?.Kind == AccountKind.Yggdrasil
                    ? account.ApiRoot
                    : null,
                AuthlibInjectorPath = authlibPath,
                MaxMemoryMb = memory,
                GameDirectory = gameDirectory
            });

            _session = session;
            session.OutputReceived += line =>
                Dispatcher.UIThread.Post(() => AppendLog(line));
            session.Exited += code =>
                Dispatcher.UIThread.Post(() =>
                {
                    IsRunning = false;
                    Status = string.Format(_strings["launch.status.exited"], code);
                });

            session.StartReading();
            Status = _strings["launch.status.started"];
        }
        catch (Exception ex)
        {
            IsRunning = false;
            Status = _strings["launch.error.launch"] + ex.Message;
            AppendLog(ex.ToString());
        }
    }

    [RelayCommand]
    private void Stop()
    {
        if (_session is not { HasExited: false })
            return;

        try
        {
            _session.Process.Kill(true);
            Status = _strings["launch.status.stopRequested"];
        }
        catch (Exception ex)
        {
            Status = _strings["launch.error.stop"] + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_services.Paths.GameDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Status = _strings["launch.error.openDir"] + ex.Message;
        }
    }

    private void AppendLog(string line)
    {
        Log += line + Environment.NewLine;

        if (Log.Length > 200_000)
            Log = Log[^200_000..];
    }

    private async Task<string> InstallJavaRuntimeAsync()
    {
        var component = await ResolveJavaComponentAsync();
        var progress = new Progress<InstallProgress>(value =>
            Status = value.Total > 0
                ? $"{value.Stage} {value.Completed}/{value.Total}"
                : value.Stage);

        return await _services.JavaInstaller.InstallAsync(component, progress);
    }

    private async Task<string> ResolveJavaComponentAsync()
    {
        try
        {
            var jsonPath = SelectedVersion?.JsonPath;

            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(jsonPath); depth++)
            {
                if (!File.Exists(jsonPath))
                    break;

                var detail = await VersionManifestService.ReadDetailAsync(jsonPath);
                if (!string.IsNullOrWhiteSpace(detail.JavaVersion?.Component))
                    return detail.JavaVersion.Component;

                jsonPath = string.IsNullOrWhiteSpace(detail.InheritsFrom)
                    ? null
                    : _services.Paths.VersionJsonPath(detail.InheritsFrom);
            }
        }
        catch (Exception)
        {
            // 回退到默认组件。
        }

        return JavaRuntimeService.DefaultComponent;
    }

    private async Task<LauncherAccount?> ResolveAccountAsync()
    {
        var account = _services.Settings.FindAccount(_services.Settings.SelectedAccountId);
        if (account is null)
            return null;

        try
        {
            if (account.Kind == AccountKind.Yggdrasil)
            {
                var refreshed = await _services.YggdrasilAuth.RefreshAsync(account);
                ReplaceAccount(refreshed);
                return refreshed;
            }

            if (account.Kind == AccountKind.Microsoft &&
                account.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5) &&
                !string.IsNullOrWhiteSpace(account.RefreshToken) &&
                !string.IsNullOrWhiteSpace(_services.Settings.MicrosoftClientId))
            {
                var token = await _services.MicrosoftAuth.RefreshAsync(
                    _services.Settings.MicrosoftClientId, account.RefreshToken);
                var profile = await _services.MicrosoftAuth.AuthenticateMinecraftAsync(
                    token.AccessToken);
                account.AccessToken = profile.AccessToken;
                account.RefreshToken = token.RefreshToken;
                account.ExpiresAt = profile.ExpiresAt;
                ReplaceAccount(account);
            }
        }
        catch (Exception)
        {
            // 令牌刷新失败时沿用已有令牌，由游戏端决定是否需要重新登录。
        }

        return account;
    }

    private void ReplaceAccount(LauncherAccount account)
    {
        var accounts = _services.Settings.Accounts;
        var index = accounts.FindIndex(item =>
            item.Kind == account.Kind && item.Id == account.Id);
        if (index >= 0)
            accounts[index] = account;

        _services.SaveSettings();
    }
}
