using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;
using PCL.Kernel.Java;

namespace PCL.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly LauncherServices _services;

    public SettingsViewModel(LauncherServices services)
    {
        _services = services;
        gameDirectory = services.Paths.GameDirectory;
        javaPath = services.Settings.JavaPath ?? "";
        memoryMb = services.Settings.MaxMemoryMb;
        username = services.Settings.OfflineUsername;
        isolateVersions = services.Settings.IsolateVersions;

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
    private string status = "设置会自动保存";

    public string PlatformSummary =>
        $"{_services.Platform.OsName} · {_services.Platform.Architecture} · {_services.Platform.DataDirectory}";

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(GameDirectory))
        {
            Status = "游戏目录不能为空";
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
            Status = "设置已保存";
        }
        catch (Exception ex)
        {
            Status = "保存失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void AddAccount()
    {
        var name = NewAccountName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = "请输入离线用户名";
            return;
        }

        if (!Accounts.Contains(name, StringComparer.OrdinalIgnoreCase))
            Accounts.Add(name);

        SelectedAccount = Accounts.First(account =>
            string.Equals(account, name, StringComparison.OrdinalIgnoreCase));
        NewAccountName = "";
        Status = $"已添加离线账户 {name}，保存后生效";
    }

    [RelayCommand]
    private void RemoveAccount()
    {
        if (string.IsNullOrWhiteSpace(SelectedAccount))
        {
            Status = "请选择要移除的账户";
            return;
        }

        if (Accounts.Count <= 1)
        {
            Status = "至少保留一个离线账户";
            return;
        }

        var removed = SelectedAccount;
        Accounts.Remove(removed);
        SelectedAccount = Accounts.FirstOrDefault();
        Status = $"已移除 {removed}，保存后生效";
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = "正在检测 Java…";

        try
        {
            var java = await JavaLocator.FindAsync();
            if (java is null)
            {
                Status = "未找到 Java，请手动填写路径";
                return;
            }

            JavaPath = java.Path;
            Status = $"已找到 {java.DisplayName}";
        }
        catch (Exception ex)
        {
            Status = "Java 检测失败：" + ex.Message;
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
            Status = "无法打开数据目录：" + ex.Message;
        }
    }
}
