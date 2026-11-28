using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Kernel.Net;

/// <summary>仓库 Issues 列表中的一条反馈。</summary>
public sealed record GitHubIssue(
    int Number,
    string Title,
    string HtmlUrl,
    DateTimeOffset CreatedAt,
    string Author,
    bool IsPullRequest)
{
    public string DisplayTitle => $"#{Number} {Title}";

    public string CreatedLabel => CreatedAt.LocalDateTime.ToString("yyyy-MM-dd");
}

/// <summary>读取公开仓库的 Issue 列表，用于启动器内的反馈页。</summary>
public sealed class GitHubIssueClient : IDisposable
{
    private const string ApiUrl =
        "https://api.github.com/repos/PCL-Community/PCL-CE/issues?state=open&per_page=15";

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public GitHubIssueClient()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PCL-CE", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<IReadOnlyList<GitHubIssue>> GetOpenIssuesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var stream = await _httpClient
            .GetStreamAsync(ApiUrl, cancellationToken)
            .ConfigureAwait(false);

        var payload = await JsonSerializer
            .DeserializeAsync<List<GitHubIssuePayload>>(stream, Options, cancellationToken)
            .ConfigureAwait(false);

        if (payload is null)
            return [];

        return payload
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new GitHubIssue(
                item.Number,
                item.Title!,
                item.HtmlUrl ?? "",
                item.CreatedAt,
                item.User?.Login ?? "",
                item.PullRequest is not null))
            .ToArray();
    }

    public static bool IsSafeIssueLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _httpClient.Dispose();

    private sealed class GitHubIssuePayload
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("user")]
        public GitHubUserPayload? User { get; set; }

        [JsonPropertyName("pull_request")]
        public JsonElement? PullRequest { get; set; }
    }

    private sealed class GitHubUserPayload
    {
        [JsonPropertyName("login")]
        public string? Login { get; set; }
    }
}
