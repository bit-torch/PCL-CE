using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Desktop.Services;
using PCL.Kernel.Install;
using PCL.Kernel.Java;
using PCL.Kernel.Launch;
using PCL.Kernel.Mojang;
using PCL.Kernel.Versions;

namespace PCL.Desktop.ViewModels;

public partial class LaunchViewModel : ObservableObject
{
    private readonly LauncherServices _services;
    private readonly Func<Task<bool>> _licenseGate;
    private GameSession? _session;

    public LaunchViewModel(LauncherServices services, Func<Task<bool>> licenseGate)
    {
        _services = services;
        _licenseGate = licenseGate;
        username = services.Settings.OfflineUsername;
        memoryMb = services.Settings.MaxMemoryMb;
        javaPath = services.Settings.JavaPath ?? "";
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
    private string status = "等待启动";

    [ObservableProperty]
    private string log = "";

    [ObservableProperty]
    private bool isRunning;

    public void RefreshList()
    {
        var previous = SelectedVersion?.Id;

        Versions.Clear();
        foreach (var version in InstalledVersionScanner.Scan(_services.Paths))
            Versions.Add(version);

        SelectedVersion = Versions.FirstOrDefault(v => v.Id == previous) ?? Versions.FirstOrDefault();
        Status = Versions.Count == 0 ? "尚未安装任何版本" : $"已发现 {Versions.Count} 个版本";
    }

    [RelayCommand]
    private void RefreshVersions() => RefreshList();

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        Status = "正在检测 Java…";

        try
        {
            var java = await JavaLocator.FindAsync();
            if (java is null)
            {
                Status = "未找到 Java，请手动填写 java 路径";
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
    private async Task InstallJavaAsync()
    {
        if (SelectedVersion is null)
        {
            Status = "请先选择一个版本";
            return;
        }

        try
        {
            Status = "正在准备 Java 运行时…";
            JavaPath = await InstallJavaRuntimeAsync();

            _services.Settings.JavaPath = JavaPath;
            _services.SaveSettings();
            Status = "Java 运行时已就绪";
        }
        catch (Exception ex)
        {
            Status = "Java 运行时安装失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsRunning)
            return;

        if (SelectedVersion is null)
        {
            Status = "请先安装并选择一个版本";
            return;
        }

        if (string.IsNullOrWhiteSpace(JavaPath) || !File.Exists(JavaPath))
        {
            await DetectJavaAsync();
            if (string.IsNullOrWhiteSpace(JavaPath) || !File.Exists(JavaPath))
            {
                Status = "未找到系统 Java，正在安装 Mojang 运行时…";
                try
                {
                    JavaPath = await InstallJavaRuntimeAsync();
                }
                catch (Exception ex)
                {
                    Status = "Java 运行时安装失败：" + ex.Message;
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
                Status = "已取消启动";
                return;
            }

            _services.Settings.LicensePrecheckAccepted = true;
            _services.SaveSettings();
        }

        IsRunning = true;
        Log = "";
        Status = "正在启动…";

        try
        {
            var memory = Math.Clamp((int)MemoryMb, 512, 65536);

            _services.Settings.OfflineUsername = Username;
            _services.Settings.MaxMemoryMb = memory;
            _services.Settings.JavaPath = JavaPath;
            _services.Settings.LaunchCount++;
            _services.SaveSettings();

            _session?.Dispose();
            var session = await _services.Launcher.LaunchAsync(new LaunchOptions
            {
                VersionId = SelectedVersion.Id,
                JavaPath = JavaPath,
                Username = Username,
                MaxMemoryMb = memory,
                GameDirectory = _services.Paths.GameDirectory
            });

            _session = session;
            session.OutputReceived += line =>
                Dispatcher.UIThread.Post(() => AppendLog(line));
            session.Exited += code =>
                Dispatcher.UIThread.Post(() =>
                {
                    IsRunning = false;
                    Status = $"游戏已退出（{code}）";
                });

            session.StartReading();
            Status = "游戏已启动";
        }
        catch (Exception ex)
        {
            IsRunning = false;
            Status = "启动失败：" + ex.Message;
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
            Status = "已请求结束游戏进程";
        }
        catch (Exception ex)
        {
            Status = "结束进程失败：" + ex.Message;
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
            Status = "无法打开游戏目录：" + ex.Message;
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
}
