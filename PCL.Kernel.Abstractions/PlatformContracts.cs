namespace PCL.Kernel.Abstractions;

/// <summary>跨平台打开外部链接，由桌面层实现。</summary>
public interface IUrlOpener
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

/// <summary>基础平台信息，由桌面层实现。</summary>
public interface IPlatformInfo
{
    string OsName { get; }
    string OsVersion { get; }
    string Architecture { get; }
    string DataDirectory { get; }
}
