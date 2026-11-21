namespace PCL.Kernel.App;

/// <summary>
/// 下载相关配置：镜像源、线程数、限速与 HTTP 代理。
/// 与旧 PCL 的下载源设置保持同一语义，但不复用其配置格式。
/// </summary>
public sealed class DownloadSettings
{
    /// <summary>自定义镜像基址，例如 https://bmclapi2.bangbang93.com；为空时使用官方源。</summary>
    public string Mirror { get; set; } = "";

    /// <summary>优先使用官方源，镜像作为回退；默认优先镜像。</summary>
    public bool PreferOfficial { get; set; }

    /// <summary>并发下载线程数。</summary>
    public int ThreadLimit { get; set; } = 8;

    /// <summary>单文件下载限速，单位 KB/s；0 表示不限速。</summary>
    public int SpeedLimitKb { get; set; }

    /// <summary>HTTP 代理地址，例如 http://127.0.0.1:7890；为空表示直连。</summary>
    public string ProxyAddress { get; set; } = "";

    public string ProxyUsername { get; set; } = "";

    public string ProxyPassword { get; set; } = "";

    public void Normalize()
    {
        Mirror = (Mirror ?? "").Trim().TrimEnd('/');
        ThreadLimit = Math.Clamp(ThreadLimit, 1, 64);
        SpeedLimitKb = Math.Clamp(SpeedLimitKb, 0, 1024 * 1024);
        ProxyAddress = (ProxyAddress ?? "").Trim();
        ProxyUsername = ProxyUsername ?? "";
        ProxyPassword = ProxyPassword ?? "";
    }
}
