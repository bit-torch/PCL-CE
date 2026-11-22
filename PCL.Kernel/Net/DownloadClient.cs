using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using PCL.Kernel.App;

namespace PCL.Kernel.Net;

/// <summary>
/// 带哈希校验的下载器。先写临时文件，校验通过后再原子替换目标文件。
/// 支持自定义镜像基址、官方源回退、限速与 HTTP 代理。
/// </summary>
public sealed class DownloadClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly DownloadSettings _settings;

    public DownloadClient(HttpClient? httpClient = null)
        : this(new DownloadSettings(), httpClient)
    {
    }

    public DownloadClient(DownloadSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient(CreateHandler(settings))
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-CE/1.0 (clean-room launcher)");
    }

    /// <summary>建议的并发下载线程数。</summary>
    public int ThreadLimit => Math.Clamp(_settings.ThreadLimit, 1, 64);

    /// <summary>当前的单文件限速（KB/s），0 表示不限速。</summary>
    public int SpeedLimitKb => Math.Max(0, _settings.SpeedLimitKb);

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        string? sha1 = null,
        long? size = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? sha512 = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("下载地址不能为空。", nameof(url));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        sha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1.Trim().ToLowerInvariant();
        sha512 = string.IsNullOrWhiteSpace(sha512) ? null : sha512.Trim().ToLowerInvariant();

        if (File.Exists(destinationPath) &&
            await VerifyAsync(destinationPath, sha1, size, cancellationToken, sha512)
                .ConfigureAwait(false))
        {
            progress?.Report(1d);
            return;
        }

        var candidates = BuildCandidates(url);
        Exception? lastError = null;

        foreach (var candidate in candidates)
        {
            var tempPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await DownloadCoreAsync(candidate, tempPath, progress, cancellationToken)
                    .ConfigureAwait(false);

                if (!await VerifyAsync(tempPath, sha1, size, cancellationToken, sha512)
                        .ConfigureAwait(false))
                    throw new InvalidDataException($"文件校验失败：{Path.GetFileName(destinationPath)}");

                File.Move(tempPath, destinationPath, true);
                progress?.Report(1d);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        throw lastError ?? new InvalidDataException($"下载失败：{Path.GetFileName(destinationPath)}");
    }

    private async Task DownloadCoreAsync(
        string url,
        string tempPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var speedLimit = SpeedLimitKb * 1024L;
        var stopwatch = Stopwatch.StartNew();

        await using (var source = await response.Content
                         .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(
                         tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                         81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                received += read;

                if (total > 0)
                    progress?.Report((double)received / total);

                if (speedLimit > 0)
                {
                    var expectedMs = received * 1000 / speedLimit;
                    var delay = expectedMs - stopwatch.ElapsedMilliseconds;
                    if (delay > 0)
                        await Task.Delay((int)Math.Min(delay, 500), cancellationToken)
                            .ConfigureAwait(false);
                }
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<string> BuildCandidates(string officialUrl)
    {
        var mirror = ResolveMirrorUrl(officialUrl, _settings.Mirror);
        if (mirror is null)
            return [officialUrl];

        return _settings.PreferOfficial
            ? [officialUrl, mirror]
            : [mirror, officialUrl];
    }

    /// <summary>
    /// 将 Mojang 官方地址转换为 BMCLAPI 风格的镜像地址；无法映射时返回 null。
    /// </summary>
    public static string? ResolveMirrorUrl(string url, string mirrorBase)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(mirrorBase))
            return null;

        var baseUrl = mirrorBase.TrimEnd('/');

        const string assetsPrefix = "https://resources.download.minecraft.net/";
        if (url.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
            return baseUrl + "/assets/" + url[assetsPrefix.Length..];

        const string librariesPrefix = "https://libraries.minecraft.net/";
        if (url.StartsWith(librariesPrefix, StringComparison.OrdinalIgnoreCase))
            return baseUrl + "/maven/" + url[librariesPrefix.Length..];

        foreach (var host in new[]
                 {
                     "https://piston-meta.mojang.com",
                     "https://piston-data.mojang.com",
                     "https://launchermeta.mojang.com",
                     "https://launcher.mojang.com"
                 })
        {
            if (url.StartsWith(host + "/", StringComparison.OrdinalIgnoreCase))
                return baseUrl + url[host.Length..];
        }

        return null;
    }

    private static SocketsHttpHandler CreateHandler(DownloadSettings settings)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        if (string.IsNullOrWhiteSpace(settings.ProxyAddress))
            return handler;

        try
        {
            var address = settings.ProxyAddress.Contains("://", StringComparison.Ordinal)
                ? settings.ProxyAddress
                : "http://" + settings.ProxyAddress;

            handler.Proxy = new WebProxy(new Uri(address))
            {
                Credentials = string.IsNullOrWhiteSpace(settings.ProxyUsername)
                    ? null
                    : new NetworkCredential(
                        settings.ProxyUsername, settings.ProxyPassword)
            };
            handler.UseProxy = true;
        }
        catch (UriFormatException)
        {
            // 代理地址无效时保持直连，避免启动器无法使用。
        }

        return handler;
    }

    public static async Task<bool> VerifyAsync(
        string path,
        string? sha1,
        long? size,
        CancellationToken cancellationToken = default,
        string? sha512 = null)
    {
        if (!File.Exists(path))
            return false;

        if (size is { } expectedSize && new FileInfo(path).Length != expectedSize)
            return false;

        if (string.IsNullOrWhiteSpace(sha1) && string.IsNullOrWhiteSpace(sha512))
            return true;

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (!string.IsNullOrWhiteSpace(sha1))
        {
            var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(hash), sha1.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(sha512))
                return true;
        }

        stream.Position = 0;
        var hash512 = await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            Convert.ToHexString(hash512), sha512!.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _httpClient.Dispose();
}
