using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
using PCL.Kernel.Accounts;
using PCL.Kernel.Java;

namespace PCL.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly LocalizationService _strings;

    public SettingsViewModel(LauncherServices services)
    {
        _services = services;
        _strings = services.Localization;
        gameDirectory = services.Paths.GameDirectory;
        javaPath = services.Settings.JavaPath ?? "";
        memoryMb = services.Settings.MaxMemoryMb;
        username = services.Settings.OfflineUsername;
        isolateVersions = services.Settings.IsolateVersions;
        status = _strings["settings.status.autoSave"];
        microsoftClientId = services.Settings.MicrosoftClientId;
        microsoftStatus = _strings["settings.auth.idle"];
        yggdrasilStatus = _strings["settings.auth.idle"];
        darkTheme = services.Settings.DarkTheme;
        checkUpdatesOnStartup = services.Settings.CheckUpdatesOnStartup;

        downloadMirror = services.Settings.Download.Mirror;
        preferOfficialDownload = services.Settings.Download.PreferOfficial;
        downloadThreadLimit = services.Settings.Download.ThreadLimit;
        downloadSpeedLimitKb = services.Settings.Download.SpeedLimitKb;
        proxyAddress = services.Settings.Download.ProxyAddress;
        proxyUsername = services.Settings.Download.ProxyUsername;
        proxyPassword = services.Settings.Download.ProxyPassword;

        jvmArguments = services.Settings.JvmArguments;
        gameArguments = services.Settings.GameArguments;
        gameWindowWidth = services.Settings.GameWindowWidth;
        gameWindowHeight = services.Settings.GameWindowHeight;
        gameFullScreen = services.Settings.GameFullScreen;
        var languageCode = string.IsNullOrWhiteSpace(services.Settings.Language)
            ? _strings.Language
            : services.Settings.Language;
        selectedLanguage = Languages.FirstOrDefault(item => item.Value == languageCode)
                           ?? Languages[0];

        AccentOptions =
        [
            new(AppearanceService.DefaultAccent, _strings["settings.appearance.accent.sky"]),
            new("#12B886", _strings["settings.appearance.accent.teal"]),
            new("#7C5CFC", _strings["settings.appearance.accent.violet"]),
            new("#F08C2E", _strings["settings.appearance.accent.amber"])
        ];

        FontOptions =
        [
            new("", _strings["settings.appearance.font.default"]),
            new("Inter", "Inter"),
            new("Microsoft YaHei UI", "微软雅黑"),
            new("PingFang SC", "苹方"),
            new("Noto Sans CJK SC", "Noto Sans CJK SC")
        ];

        selectedAccent = AccentOptions.FirstOrDefault(option =>
                             string.Equals(
                                 option.Value, services.Settings.AccentColor,
                                 StringComparison.OrdinalIgnoreCase))
                         ?? AccentOptions[0];

        selectedFont = FontOptions.FirstOrDefault(option =>
                           string.Equals(
                               option.Value, services.Settings.UIFontFamily,
                               StringComparison.OrdinalIgnoreCase))
                       ?? FontOptions[0];

        foreach (var path in services.Settings.JavaPaths)
            JavaRuntimes.Add(new JavaRuntime(path, null));

        foreach (var account in services.Settings.Accounts)
            Accounts.Add(account);

        selectedAccount = Accounts.FirstOrDefault(account =>
                              account.Id == services.Settings.SelectedAccountId)
                          ?? Accounts.FirstOrDefault();
    }

    public ObservableCollection<LauncherAccount> Accounts { get; } = [];

    public ObservableCollection<JavaRuntime> JavaRuntimes { get; } = [];

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English")
    ];

    public IReadOnlyList<AccentOption> AccentOptions { get; }

    public IReadOnlyList<FontOption> FontOptions { get; }

    [ObservableProperty]
    private AccentOption selectedAccent;

    [ObservableProperty]
    private FontOption selectedFont;

    partial void OnSelectedAccentChanged(AccentOption value)
    {
        if (value is null)
            return;

        AppearanceService.ApplyAccent(value.Value);
        _services.Settings.AccentColor = value.Value;
        _services.SaveSettings();
    }

    partial void OnSelectedFontChanged(FontOption value)
    {
        if (value is null)
            return;

        AppearanceService.ApplyFont(value.Value);
        _services.Settings.UIFontFamily = value.Value;
        _services.SaveSettings();
    }

    [ObservableProperty]
    private JavaRuntime? selectedJavaRuntime;

    [ObservableProperty]
    private string newJavaPath = "";

    [ObservableProperty]
    private bool isScanningJava;

    [ObservableProperty]
    private bool darkTheme;

    [ObservableProperty]
    private bool checkUpdatesOnStartup;

    [ObservableProperty]
    private string downloadMirror;

    [ObservableProperty]
    private bool preferOfficialDownload;

    [ObservableProperty]
    private double downloadThreadLimit;

    [ObservableProperty]
    private double downloadSpeedLimitKb;

    [ObservableProperty]
    private string proxyAddress;

    [ObservableProperty]
    private string proxyUsername;

    [ObservableProperty]
    private string proxyPassword;

    [ObservableProperty]
    private string jvmArguments;

    [ObservableProperty]
    private string gameArguments;

    [ObservableProperty]
    private double gameWindowWidth;

    [ObservableProperty]
    private double gameWindowHeight;

    [ObservableProperty]
    private bool gameFullScreen;

    [ObservableProperty]
    private LanguageOption selectedLanguage;

    public bool HasSelectedJavaRuntime => SelectedJavaRuntime is not null;

    partial void OnSelectedJavaRuntimeChanged(JavaRuntime? value) =>
        OnPropertyChanged(nameof(HasSelectedJavaRuntime));

    partial void OnDarkThemeChanged(bool value)
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = value
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
    }

    [ObservableProperty]
    private LauncherAccount? selectedAccount;

    [ObservableProperty]
    private string newAccountName = "";

    [ObservableProperty]
    private bool isolateVersions;

    partial void OnSelectedAccountChanged(LauncherAccount? value)
    {
        if (value is not null)
            Username = value.Name;
    }

    [ObservableProperty]
    private string gameDirectory;

    [ObservableProperty]
    private string javaPath;

    [ObservableProperty]
    private double memoryMb;

    [ObservableProperty]
    private string username;

    [ObservableProperty]
    private string status;

    [ObservableProperty]
    private string microsoftClientId;

    [ObservableProperty]
    private string microsoftStatus;

    [ObservableProperty]
    private string deviceCode = "";

    [ObservableProperty]
    private string verificationUri = "";

    public bool HasDeviceCode => !string.IsNullOrEmpty(DeviceCode);

    public bool HasVerificationUri => !string.IsNullOrEmpty(VerificationUri);

    partial void OnDeviceCodeChanged(string value) => OnPropertyChanged(nameof(HasDeviceCode));

    partial void OnVerificationUriChanged(string value) =>
        OnPropertyChanged(nameof(HasVerificationUri));

    [ObservableProperty]
    private bool isMicrosoftBusy;

    [ObservableProperty]
    private string yggdrasilApiRoot = "";

    [ObservableProperty]
    private string yggdrasilUsername = "";

    [ObservableProperty]
    private string yggdrasilPassword = "";

    [ObservableProperty]
    private string yggdrasilStatus;

    [ObservableProperty]
    private bool isYggdrasilBusy;

    public string PlatformSummary =>
        $"{_services.Platform.OsName} · {_services.Platform.Architecture} · {_services.Platform.DataDirectory}";

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(GameDirectory))
        {
            Status = _strings["settings.error.emptyDir"];
            return;
        }

        try
        {
            _services.Paths.GameDirectory = GameDirectory;
            _services.Paths.EnsureDirectories();
            _services.Settings.JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
            _services.Settings.MaxMemoryMb = Math.Clamp((int)MemoryMb, 512, 65536);
            _services.Settings.IsolateVersions = IsolateVersions;
            _services.Settings.MicrosoftClientId = MicrosoftClientId.Trim();
            _services.Settings.DarkTheme = DarkTheme;
            _services.Settings.AccentColor = SelectedAccent.Value;
            _services.Settings.UIFontFamily = SelectedFont.Value;
            _services.Settings.Language = SelectedLanguage.Value;
            _services.Settings.CheckUpdatesOnStartup = CheckUpdatesOnStartup;

            _services.Settings.Download.Mirror = DownloadMirror.Trim();
            _services.Settings.Download.PreferOfficial = PreferOfficialDownload;
            _services.Settings.Download.ThreadLimit =
                Math.Clamp((int)DownloadThreadLimit, 1, 64);
            _services.Settings.Download.SpeedLimitKb =
                Math.Max(0, (int)DownloadSpeedLimitKb);
            _services.Settings.Download.ProxyAddress = ProxyAddress.Trim();
            _services.Settings.Download.ProxyUsername = ProxyUsername.Trim();
            _services.Settings.Download.ProxyPassword = ProxyPassword;
            _services.Settings.Download.Normalize();

            _services.Settings.JvmArguments = JvmArguments.Trim();
            _services.Settings.GameArguments = GameArguments.Trim();
            _services.Settings.GameWindowWidth =
                Math.Clamp((int)GameWindowWidth, 0, 16384);
            _services.Settings.GameWindowHeight =
                Math.Clamp((int)GameWindowHeight, 0, 16384);
            _services.Settings.GameFullScreen = GameFullScreen;

            if (SelectedAccount is { Kind: AccountKind.Offline } offline)
            {
                var name = string.IsNullOrWhiteSpace(Username) ? offline.Name : Username.Trim();
                offline.Name = name;
                offline.Id = LauncherAccount.Offline(name).Id;
                _services.Settings.OfflineUsername = name;
            }

            SyncAccountSettings();
            _services.SaveSettings();

            GameDirectory = _services.Paths.GameDirectory;
            Status = _strings["settings.status.saved"];
        }
        catch (Exception ex)
        {
            Status = _strings["settings.error.save"] + ex.Message;
        }
    }

    [RelayCommand]
    private void AddAccount()
    {
        var name = NewAccountName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = _strings["settings.error.username"];
            return;
        }

        var existing = Accounts.FirstOrDefault(account =>
            account.Kind == AccountKind.Offline &&
            string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = LauncherAccount.Offline(name);
            Accounts.Add(existing);
        }

        SelectedAccount = existing;
        NewAccountName = "";
        Status = string.Format(_strings["settings.status.added"], name);
    }

    [RelayCommand]
    private void RemoveAccount()
    {
        if (SelectedAccount is null)
        {
            Status = _strings["settings.error.selectAccount"];
            return;
        }

        if (Accounts.Count <= 1)
        {
            Status = _strings["settings.error.lastAccount"];
            return;
        }

        var removed = SelectedAccount;
        var index = Accounts.IndexOf(removed);
        Accounts.Remove(removed);
        SelectedAccount = Accounts[Math.Clamp(index, 0, Accounts.Count - 1)];
        Status = string.Format(_strings["settings.status.removed"], removed.Name);
    }

    [RelayCommand]
    private async Task BeginMicrosoftLoginAsync()
    {
        if (IsMicrosoftBusy)
            return;

        var clientId = string.IsNullOrWhiteSpace(MicrosoftClientId)
            ? _services.Settings.MicrosoftClientId
            : MicrosoftClientId.Trim();

        if (string.IsNullOrWhiteSpace(clientId))
        {
            MicrosoftStatus = _strings["settings.auth.ms.noClientId"];
            return;
        }

        IsMicrosoftBusy = true;
        DeviceCode = "";
        VerificationUri = "";

        try
        {
            var info = await _services.MicrosoftAuth.RequestDeviceCodeAsync(clientId);
            DeviceCode = info.UserCode;
            VerificationUri = info.VerificationUriComplete ?? info.VerificationUri;
            MicrosoftStatus = string.Format(
                _strings["settings.auth.ms.waiting"], info.UserCode, info.VerificationUri);

            var progress = new Progress<string>(message => MicrosoftStatus = message);
            var token = await _services.MicrosoftAuth.WaitForTokenAsync(
                clientId, info, progress);
            var profile = await _services.MicrosoftAuth.AuthenticateMinecraftAsync(token.AccessToken);

            var account = new LauncherAccount
            {
                Id = profile.Id,
                Name = profile.Name,
                Kind = AccountKind.Microsoft,
                AccessToken = profile.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAt = profile.ExpiresAt
            };

            UpsertAccount(account);
            _services.Settings.MicrosoftClientId = clientId;
            SyncAccountSettings();
            _services.SaveSettings();

            SelectedAccount = account;
            DeviceCode = "";
            MicrosoftStatus = string.Format(_strings["settings.auth.ms.success"], account.Name);
        }
        catch (Exception ex)
        {
            MicrosoftStatus = _strings["settings.auth.error"] + ex.Message;
        }
        finally
        {
            IsMicrosoftBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenVerificationUriAsync()
    {
        if (string.IsNullOrWhiteSpace(VerificationUri))
            return;

        try
        {
            await _services.UrlOpener.OpenAsync(new Uri(VerificationUri));
        }
        catch (Exception ex)
        {
            MicrosoftStatus = _strings["settings.auth.error"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoginYggdrasilAsync()
    {
        if (IsYggdrasilBusy)
            return;

        if (string.IsNullOrWhiteSpace(YggdrasilApiRoot) ||
            string.IsNullOrWhiteSpace(YggdrasilUsername) ||
            string.IsNullOrWhiteSpace(YggdrasilPassword))
        {
            YggdrasilStatus = _strings["settings.auth.ygg.required"];
            return;
        }

        IsYggdrasilBusy = true;
        try
        {
            YggdrasilStatus = _strings["settings.auth.ygg.working"];
            var account = await _services.YggdrasilAuth.AuthenticateAsync(
                YggdrasilApiRoot, YggdrasilUsername, YggdrasilPassword);

            UpsertAccount(account);
            SyncAccountSettings();
            _services.SaveSettings();

            SelectedAccount = account;
            YggdrasilPassword = "";
            YggdrasilStatus = string.Format(_strings["settings.auth.ygg.success"], account.Name);
        }
        catch (Exception ex)
        {
            YggdrasilStatus = _strings["settings.auth.error"] + ex.Message;
        }
        finally
        {
            IsYggdrasilBusy = false;
        }
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = _strings["settings.status.detectingJava"];

        try
        {
            var java = await JavaLocator.FindAsync();
            if (java is null)
            {
                Status = _strings["settings.status.javaMissing"];
                return;
            }

            JavaPath = java.Path;
            UpsertJava(java);
            PersistJavaPaths();
            Status = string.Format(_strings["settings.status.javaFound"], java.DisplayName);
        }
        catch (Exception ex)
        {
            Status = _strings["settings.error.java"] + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ScanJavaAsync()
    {
        if (IsScanningJava)
            return;

        IsScanningJava = true;
        Status = _strings["settings.status.scanningJava"];
        try
        {
            var found = await JavaLocator.FindAllAsync();
            foreach (var java in found)
                UpsertJava(java);

            PersistJavaPaths();
            Status = string.Format(_strings["settings.status.javaScanned"], found.Count);
        }
        catch (Exception ex)
        {
            Status = _strings["settings.error.java"] + ex.Message;
        }
        finally
        {
            IsScanningJava = false;
        }
    }

    [RelayCommand]
    private async Task AddJavaAsync()
    {
        var path = NewJavaPath.Trim().Trim('"');
        if (path.Length == 0)
        {
            Status = _strings["settings.java.pathRequired"];
            return;
        }

        try
        {
            var java = await JavaLocator.ProbeAsync(path);
            if (java is null)
            {
                Status = _strings["settings.java.invalid"];
                return;
            }

            UpsertJava(java);
            PersistJavaPaths();
            NewJavaPath = "";
            SelectedJavaRuntime = java;
            Status = string.Format(_strings["settings.status.javaAdded"], java.DisplayName);
        }
        catch (Exception ex)
        {
            Status = _strings["settings.error.java"] + ex.Message;
        }
    }

    [RelayCommand]
    private void UseSelectedJava()
    {
        if (SelectedJavaRuntime is null)
        {
            Status = _strings["settings.java.selectRequired"];
            return;
        }

        JavaPath = SelectedJavaRuntime.Path;
        _services.Settings.JavaPath = JavaPath;
        _services.SaveSettings();
        Status = string.Format(
            _strings["settings.status.javaSelected"], SelectedJavaRuntime.DisplayName);
    }

    [RelayCommand]
    private void RemoveJava()
    {
        if (SelectedJavaRuntime is null)
        {
            Status = _strings["settings.java.selectRequired"];
            return;
        }

        var removed = SelectedJavaRuntime;
        JavaRuntimes.Remove(removed);
        if (string.Equals(JavaPath, removed.Path, StringComparison.OrdinalIgnoreCase))
            JavaPath = "";

        PersistJavaPaths();
        SelectedJavaRuntime = JavaRuntimes.FirstOrDefault();
        Status = string.Format(_strings["settings.status.javaRemoved"], removed.DisplayName);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_services.Paths.DataDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Status = _strings["settings.error.openData"] + ex.Message;
        }
    }

    private void UpsertJava(JavaRuntime java)
    {
        for (var i = 0; i < JavaRuntimes.Count; i++)
        {
            if (!string.Equals(
                    JavaRuntimes[i].Path, java.Path, StringComparison.OrdinalIgnoreCase))
                continue;

            JavaRuntimes[i] = java;
            return;
        }

        JavaRuntimes.Add(java);
    }

    private void PersistJavaPaths()
    {
        _services.Settings.JavaPaths = JavaRuntimes.Select(java => java.Path).ToList();
        _services.SaveSettings();
    }

    private void UpsertAccount(LauncherAccount account)
    {
        var existing = Accounts.FirstOrDefault(item =>
            item.Kind == account.Kind &&
            string.Equals(item.Id, account.Id, StringComparison.Ordinal));

        if (existing is null)
        {
            Accounts.Add(account);
            return;
        }

        var index = Accounts.IndexOf(existing);
        Accounts[index] = account;
    }

    private void SyncAccountSettings()
    {
        _services.Settings.Accounts = Accounts.ToList();
        _services.Settings.OfflineAccounts = Accounts
            .Where(account => account.Kind == AccountKind.Offline)
            .Select(account => account.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _services.Settings.SelectedAccountId = SelectedAccount?.Id;
    }
}

public sealed record LanguageOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record AccentOption(string Value, string Label)
{
    public IBrush Swatch => new SolidColorBrush(Color.Parse(Value));

    public override string ToString() => Label;
}

public sealed record FontOption(string Value, string Label)
{
    public override string ToString() => Label;
}
