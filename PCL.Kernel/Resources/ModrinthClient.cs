using System.Text.Json.Nodes;

namespace PCL.Kernel.Resources;

/// <summary>
/// Modrinth 公开 API 客户端。只使用官方 v2 接口，不携带任何私有密钥。
/// </summary>
public sealed class ModrinthClient : IDisposable
{
    private const string BaseUrl = "https://api.modrinth.com/v2/";
    private const string UserAgent = "PCL-CE/1.0 (modrinth client)";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public ModrinthClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<ResourceSearchResult> SearchAsync(
        string query,
        ResourceType type,
        string? gameVersion = null,
        string? loader = null,
        int limit = 20,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var facets = new List<string>
        {
            $"[\"project_type:{ProjectTypeName(type)}\"]"
        };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add($"[\"versions:{gameVersion.Trim()}\"]");

        if (type == ResourceType.Mod && !string.IsNullOrWhiteSpace(loader))
            facets.Add($"[\"categories:{loader.Trim()}\"]");

        var url = $"search?query={Uri.EscapeDataString(query ?? "")}" +
                  $"&limit={Math.Clamp(limit, 1, 50)}&offset={Math.Max(0, offset)}" +
                  "&index=relevance" +
                  $"&facets={Uri.EscapeDataString("[" + string.Join(",", facets) + "]")}";

        var node = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var projects = (node?["hits"] as JsonArray)?.OfType<JsonObject>()
            .Select(ParseProject)
            .Where(project => project is not null)
            .Select(project => project!)
            .ToArray() ?? [];

        var total = node?["total_hits"]?.GetValue<int>() ?? projects.Length;
        return new ResourceSearchResult(projects, total);
    }

    public async Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion.Trim()}\"]")}");
        if (!string.IsNullOrWhiteSpace(loader))
            query.Add($"loaders={Uri.EscapeDataString($"[\"{loader.Trim()}\"]")}");

        var url = $"project/{Uri.EscapeDataString(projectId)}/version" +
                  (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var node = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return (node as JsonArray)?.OfType<JsonObject>()
            .Select(ParseVersion)
            .ToArray() ?? [];
    }

    public async Task<ResourceVersion?> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var node = await GetJsonAsync(
                $"version/{Uri.EscapeDataString(versionId)}", cancellationToken)
            .ConfigureAwait(false);

        return node is JsonObject version ? ParseVersion(version) : null;
    }

    /// <summary>
    /// 递归解析必选依赖。已访问过的项目不会重复加入；可选依赖不自动安装。
    /// </summary>
    public async Task<IReadOnlyList<ResourceVersion>> ResolveRequiredDependenciesAsync(
        ResourceVersion version,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ResourceVersion>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            version.ProjectId
        };

        var queue = new Queue<ResourceVersion>();
        queue.Enqueue(version);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var dependency in current.Dependencies.Where(item => item.IsRequired))
            {
                if (string.IsNullOrWhiteSpace(dependency.ProjectId) ||
                    !visited.Add(dependency.ProjectId!))
                    continue;

                ResourceVersion? resolved;
                if (!string.IsNullOrWhiteSpace(dependency.VersionId))
                {
                    resolved = await GetVersionAsync(dependency.VersionId!, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    resolved = (await GetVersionsAsync(
                            dependency.ProjectId!, gameVersion, loader, cancellationToken)
                        .ConfigureAwait(false)).FirstOrDefault();
                }

                if (resolved is null)
                    continue;

                visited.Add(resolved.ProjectId);
                result.Add(resolved);
                queue.Enqueue(resolved);
            }
        }

        return result;
    }

    private async Task<JsonNode?> GetJsonAsync(
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(BaseUrl + relativeUrl, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new HttpRequestException(
                $"Modrinth 返回 {(int)response.StatusCode}：{Trim(body)}");
        }

        var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static ResourceProject? ParseProject(JsonObject node)
    {
        var id = node["project_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return new ResourceProject(
            id,
            node["slug"]?.ToString() ?? id,
            node["title"]?.ToString() ?? id,
            node["description"]?.ToString() ?? "",
            node["author"]?.ToString() ?? "",
            node["icon_url"]?.ToString(),
            node["downloads"]?.GetValue<long>() ?? 0,
            node["project_type"]?.ToString() ?? "",
            ParseStringArray(node["categories"]),
            ParseStringArray(node["versions"]));
    }

    private static ResourceVersion ParseVersion(JsonObject node)
    {
        var files = (node["files"] as JsonArray)?.OfType<JsonObject>()
            .Select(file => new ResourceFile(
                file["url"]?.ToString() ?? "",
                file["filename"]?.ToString() ?? "",
                file["primary"]?.GetValue<bool>() ?? false,
                file["size"]?.GetValue<long>() ?? 0,
                file["hashes"]?["sha1"]?.ToString()))
            .Where(file => !string.IsNullOrWhiteSpace(file.Url))
            .ToArray() ?? [];

        var dependencies = (node["dependencies"] as JsonArray)?.OfType<JsonObject>()
            .Select(dependency => new ResourceDependency(
                dependency["version_id"]?.ToString(),
                dependency["project_id"]?.ToString(),
                dependency["dependency_type"]?.ToString() ?? "required"))
            .ToArray() ?? [];

        return new ResourceVersion(
            node["id"]?.ToString() ?? "",
            node["project_id"]?.ToString() ?? "",
            node["name"]?.ToString() ?? "",
            node["version_number"]?.ToString() ?? "",
            node["version_type"]?.ToString() ?? "release",
            ParseStringArray(node["game_versions"]),
            ParseStringArray(node["loaders"]),
            files,
            dependencies);
    }

    private static string[] ParseStringArray(JsonNode? node) =>
        (node as JsonArray)?.Select(item => item?.ToString() ?? "")
        .Where(value => value.Length > 0)
        .ToArray() ?? [];

    private static string ProjectTypeName(ResourceType type) => type switch
    {
        ResourceType.Mod => "mod",
        ResourceType.ResourcePack => "resourcepack",
        ResourceType.Shader => "shader",
        ResourceType.DataPack => "datapack",
        _ => "mod"
    };

    private static string Trim(string value) =>
        value.Length <= 400 ? value : value[..400];

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
