using System.Text.Json.Nodes;

namespace PCL.Kernel.Updates;

/// <summary>一次更新检查的结果。</summary>
public sealed record UpdateInfo(
    bool IsAvailable,
    string CurrentVersion,
    string LatestVersion,
    string? DownloadUrl,
    string? ReleaseNotes,
    string? HtmlUrl);

/// <summary>
/// 通过 GitHub Releases 检查启动器更新。仓库由构造参数指定，不读取上游 PCL 的更新源。
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    public const string DefaultOwner = "PCL-Community";
    public const string DefaultRepo = "PCL-CE";

    private const string UserAgent = "PCL-CE/1.0 (update checker)";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _owner;
    private readonly string _repo;

    public UpdateChecker(
        HttpClient? httpClient = null,
        string owner = DefaultOwner,
        string repo = DefaultRepo)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _owner = owner;
        _repo = repo;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public string ReleasesApiUrl =>
        $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

    public static string CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<UpdateInfo> CheckAsync(
        string? currentVersion = null,
        CancellationToken cancellationToken = default)
    {
        currentVersion = string.IsNullOrWhiteSpace(currentVersion)
            ? CurrentVersion
            : currentVersion.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new HttpRequestException(
                $"GitHub 返回 {(int)response.StatusCode}：{Trim(body)}");
        }

        var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var tag = node?["tag_name"]?.ToString() ?? "";
        var htmlUrl = node?["html_url"]?.ToString();
        var downloadUrl = (node?["assets"] as JsonArray)?
                              .OfType<JsonObject>()
                              .Select(asset => asset["browser_download_url"]?.ToString())
                              .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                          ?? htmlUrl;

        return new UpdateInfo(
            IsNewer(tag, currentVersion),
            currentVersion,
            Normalize(tag),
            downloadUrl,
            node?["body"]?.ToString(),
            htmlUrl);
    }

    /// <summary>比较版本号，忽略前导 v 与非数字后缀，例如 v2.10.1 高于 2.9.9。</summary>
    public static bool IsNewer(string candidate, string current)
    {
        var left = ParseVersion(candidate);
        var right = ParseVersion(current);
        var length = Math.Max(left.Count, right.Count);

        for (var index = 0; index < length; index++)
        {
            var a = index < left.Count ? left[index] : 0;
            var b = index < right.Count ? right[index] : 0;
            if (a != b)
                return a > b;
        }

        return false;
    }

    private static string Normalize(string version) =>
        version.StartsWith('v') || version.StartsWith('V') ? version[1..] : version;

    private static List<int> ParseVersion(string version)
    {
        var result = new List<int>();
        foreach (var segment in Normalize(version.Trim()).Split('.', '-', '+', '_', ' '))
        {
            var digits = new string(segment.TakeWhile(char.IsDigit).ToArray());
            result.Add(int.TryParse(digits, out var value) ? value : 0);
        }

        return result;
    }

    private static string Trim(string value) =>
        value.Length <= 400 ? value : value[..400];

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
