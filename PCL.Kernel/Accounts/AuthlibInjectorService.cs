using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PCL.Kernel.Net;

namespace PCL.Kernel.Accounts;

/// <summary>下载并缓存 authlib-injector，用于第三方 Yggdrasil 账户启动。</summary>
public sealed class AuthlibInjectorService
{
    private const string LatestUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";

    private readonly LauncherPaths _paths;
    private readonly DownloadClient _downloads;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AuthlibInjectorService(LauncherPaths paths, DownloadClient downloads)
    {
        _paths = paths;
        _downloads = downloads;
    }

    public async Task<string> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(
            _paths.CacheDirectory, "authlib-injector", "authlib-injector.jar");

        if (File.Exists(target))
            return target;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(target))
                return target;

            var response = await JsonHttp.GetJsonAsync(LatestUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccess || response.Body is not JsonObject body)
                throw new InvalidOperationException($"获取 authlib-injector 信息失败：{response.ErrorMessage}");

            var downloadUrl = body["download_url"]?.ToString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new InvalidDataException("authlib-injector 响应缺少下载地址。");

            await _downloads.DownloadAsync(downloadUrl, target,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (body["checksums"]?["sha256"]?.ToString() is { Length: > 0 } sha256)
                VerifySha256(target, sha256);

            return target;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void VerifySha256(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actual, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException("authlib-injector 校验失败，请重试。");
    }
}
