using System.Diagnostics;
using PCL.Kernel;
using PCL.Kernel.Abstractions;

namespace PCL.Desktop.Services;

public sealed class SystemUrlOpener : IUrlOpener
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("只允许打开 http/https 链接。", nameof(uri));

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}

public sealed class DesktopPlatformInfo(LauncherPaths paths) : IPlatformInfo
{
    public string OsName => PlatformInfo.DisplayName;

    public string OsVersion => PlatformInfo.OsVersion;

    public string Architecture => PlatformInfo.Architecture;

    public string DataDirectory => paths.DataDirectory;
}
