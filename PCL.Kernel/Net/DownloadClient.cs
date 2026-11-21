using System.Security.Cryptography;

namespace PCL.Kernel.Net;

/// <summary>
/// 带哈希校验的下载器。先写临时文件，校验通过后再原子替换目标文件。
/// </summary>
public sealed class DownloadClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public DownloadClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-CE/1.0 (clean-room launcher)");
    }

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        string? sha1 = null,
        long? size = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("下载地址不能为空。", nameof(url));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        sha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1.Trim().ToLowerInvariant();

        if (File.Exists(destinationPath) &&
            await VerifyAsync(destinationPath, sha1, size, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(1d);
            return;
        }

        var tempPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using var response = await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? size ?? -1;
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
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!await VerifyAsync(tempPath, sha1, size, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException($"文件校验失败：{Path.GetFileName(destinationPath)}");

            File.Move(tempPath, destinationPath, true);
            progress?.Report(1d);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static async Task<bool> VerifyAsync(
        string path,
        string? sha1,
        long? size,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return false;

        if (size is { } expectedSize && new FileInfo(path).Length != expectedSize)
            return false;

        if (string.IsNullOrWhiteSpace(sha1))
            return true;

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, sha1.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _httpClient.Dispose();
}
