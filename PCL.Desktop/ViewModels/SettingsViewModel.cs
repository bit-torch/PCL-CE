using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Localization;
using PCL.Desktop.Services;
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

        foreach (var account in services.Settings.OfflineAccounts)
            Accounts.Add(account);

        selectedAccount = Username;
    }

    public ObservableCollection<string> Accounts { get; } = [];

    [ObservableProperty]
    private string? selectedAccount;

    [ObservableProperty]
    private string newAccountName = "";

    [ObservableProperty]
    private bool isolateVersions;

    partial void OnSelectedAccountChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Username = value;
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

            var username = string.IsNullOrWhiteSpace(Username) ? "Player" : Username.Trim();
            if (!Accounts.Contains(username, StringComparer.OrdinalIgnoreCase))
                Accounts.Add(username);

            Username = username;
            SelectedAccount = username;
            _services.Settings.OfflineUsername = username;
            _services.Settings.OfflineAccounts = Accounts.ToList();
            _services.Settings.IsolateVersions = IsolateVersions;
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

        if (!Accounts.Contains(name, StringComparer.OrdinalIgnoreCase))
            Accounts.Add(name);

        SelectedAccount = Accounts.First(account =>
            string.Equals(account, name, StringComparison.OrdinalIgnoreCase));
        NewAccountName = "";
        Status = string.Format(_strings["settings.status.added"], name);
    }

    [RelayCommand]
    private void RemoveAccount()
    {
        if (string.IsNullOrWhiteSpace(SelectedAccount))
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
        Accounts.Remove(removed);
        SelectedAccount = Accounts.FirstOrDefault();
        Status = string.Format(_strings["settings.status.removed"], removed);
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
            Status = string.Format(_strings["settings.status.javaFound"], java.DisplayName);
        }
        catch (Exception ex)
        {
            Status = _strings["settings.error.java"] + ex.Message;
        }
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
}
