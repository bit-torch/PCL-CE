using System.Net;
using System.Text.Json;

namespace PCL.Kernel.Homepage;

/// <summary>
/// 读取 Minecraft 官网信息流。链接在打开前会做主机白名单校验，
/// 避免信任远端返回的任意地址。
/// </summary>
public sealed class NewsService : IDisposable
{
    private const string BaseApiUrl =
        "https://net-secondary.web.minecraft-services.net/api/v1.0/zh-cn/search";

    private const int DefaultPageSize = 24;
    private const string UserAgent = "PCL-CE/1.0 (news)";

    private static readonly string[] AllowedNewsLinkHosts =
    [
        "minecraft.net",
        "minecraft-services.net",
        "microsoft.com"
    ];

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public NewsService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<IReadOnlyList<NewsItem>> GetLatestAsync(
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{BaseApiUrl}?pageSize={pageSize}&sortType=Recent&category=News&newsOnly=true&page={page}";

        using var response = await _httpClient
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var payload = await JsonSerializer
            .DeserializeAsync<NewsResponse>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Result?.Results is not { } items)
            return [];

        foreach (var item in items)
            item.Description = WebUtility.HtmlDecode(item.Description ?? "");

        return items;
    }

    /// <summary>只允许打开 Minecraft 官方域名及其子域名。</summary>
    public static bool IsSafeNewsLink(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.IdnHost;
        foreach (var allowedHost in AllowedNewsLinkHosts)
        {
            if (host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{allowedHost}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
