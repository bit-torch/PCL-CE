using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PCL.CrossPlatform;

public enum ResourceProvider { Modrinth, CurseForge }
public enum ResourceKind { Mod, Modpack, ResourcePack, Shader }
public sealed record ResourceProject(ResourceProvider Provider, string Id, string Title, string Description, string WebUrl)
{
    public override string ToString() => $"{Title}\n{Description}";
}
public sealed record ResourceRelease(string Id, string Name, JsonObject Data)
{
    public override string ToString() => Name;
}

/// <summary>Resource search, dependency installation and update lookup for either provider.</summary>
public sealed class ResourceCatalog(HttpClient client, string? curseForgeKey = null)
{
    private const string Modrinth = "https://api.modrinth.com/v2/";
    private const string CurseForge = "https://api.curseforge.com/v1/";
    private static string Escape(string value) => Uri.EscapeDataString(value);

    public async Task<IReadOnlyList<ResourceProject>> SearchAsync(ResourceProvider provider, ResourceKind kind, string query,
        string gameVersion, string loader, CancellationToken token)
    {
        if (provider == ResourceProvider.Modrinth)
        {
            var type = kind switch { ResourceKind.Mod => "mod", ResourceKind.Modpack => "modpack", ResourceKind.Shader => "shader", _ => "resourcepack" };
            var facets = new List<string[]> { new[] { "project_type:" + type } };
            if (gameVersion.Length != 0) facets.Add(["versions:" + gameVersion]);
            if (loader.Length != 0 && kind == ResourceKind.Mod) facets.Add(["categories:" + loader]);
            var result = await GetJsonAsync(Modrinth + "search?limit=30&query=" + Escape(query) + "&facets=" + Escape(System.Text.Json.JsonSerializer.Serialize(facets)), false, token);
            return result["hits"]!.AsArray().Select(item => new ResourceProject(provider, item!["project_id"]!.ToString(), item["title"]!.ToString(),
                item["description"]!.ToString(), "https://modrinth.com/" + type + "/" + item["slug"])).ToArray();
        }
        var classId = kind switch { ResourceKind.Mod => 6, ResourceKind.Modpack => 4471, ResourceKind.Shader => 6552, _ => 12 };
        var json = await GetJsonAsync(CurseForge + $"mods/search?gameId=432&classId={classId}&pageSize=30&searchFilter={Escape(query)}&gameVersion={Escape(gameVersion)}", true, token);
        return json["data"]!.AsArray().Select(item => new ResourceProject(provider, item!["id"]!.ToString(), item["name"]!.ToString(),
            item["summary"]!.ToString(), item["links"]?["websiteUrl"]?.ToString() ?? "https://www.curseforge.com/minecraft")).ToArray();
    }

    public async Task<IReadOnlyList<ResourceRelease>> VersionsAsync(ResourceProject project, string gameVersion, string loader, CancellationToken token)
    {
        JsonArray versions;
        if (project.Provider == ResourceProvider.Modrinth)
        {
            var url = Modrinth + "project/" + Escape(project.Id) + "/version";
            var parameters = new List<string>();
            if (gameVersion.Length != 0) parameters.Add("game_versions=" + Escape(System.Text.Json.JsonSerializer.Serialize(new[] { gameVersion })));
            if (loader.Length != 0) parameters.Add("loaders=" + Escape(System.Text.Json.JsonSerializer.Serialize(new[] { loader })));
            versions = (await GetJsonAsync(url + (parameters.Count == 0 ? "" : "?" + string.Join('&', parameters)), false, token)).AsArray();
            return versions.Select(item => new ResourceRelease(item!["id"]!.ToString(), item["name"]!.ToString(), item.AsObject())).ToArray();
        }
        versions = (await GetJsonAsync(CurseForge + $"mods/{Escape(project.Id)}/files?gameVersion={Escape(gameVersion)}&pageSize=50", true, token))["data"]!.AsArray();
        return versions.Where(item => loader.Length == 0 || item?["gameVersions"]?.AsArray().Any(value => value?.ToString().Equals(loader, StringComparison.OrdinalIgnoreCase) == true) == true)
            .Select(item => new ResourceRelease(item!["id"]!.ToString(), item["displayName"]!.ToString(), item.AsObject())).ToArray();
    }

    public async Task<IReadOnlyList<string>> InstallAsync(ResourceProvider provider, ResourceRelease release, string destination,
        string gameVersion, string loader, CancellationToken token)
    {
        var installed = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        await InstallRelease(release);
        return installed;

        async Task InstallRelease(ResourceRelease current)
        {
            if (!visited.Add(current.Id)) return;
            if (visited.Count > 256) throw new InvalidDataException("资源依赖数量过多。");
            foreach (var dependency in (current.Data["dependencies"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                if (provider == ResourceProvider.Modrinth)
                {
                    if (dependency["dependency_type"]?.ToString() != "required") continue;
                    if (dependency["version_id"] is { } versionId)
                    {
                        var value = (await GetJsonAsync(Modrinth + "version/" + Escape(versionId.ToString()), false, token)).AsObject();
                        await InstallRelease(new(value["id"]!.ToString(), value["name"]!.ToString(), value));
                    }
                    else if (dependency["project_id"] is { } projectId)
                    {
                        var versions = await VersionsAsync(new(provider, projectId.ToString(), "", "", ""), gameVersion, loader, token);
                        await InstallRelease(versions.FirstOrDefault() ?? throw new InvalidOperationException("缺少兼容的必需依赖：" + projectId));
                    }
                }
                else if (dependency["relationType"]?.GetValue<int>() == 3)
                {
                    var versions = await VersionsAsync(new(provider, dependency["modId"]!.ToString(), "", "", ""), gameVersion, loader, token);
                    await InstallRelease(versions.FirstOrDefault() ?? throw new InvalidOperationException("缺少兼容依赖。"));
                }
            }
            var files = provider == ResourceProvider.Modrinth ? current.Data["files"]!.AsArray().OfType<JsonObject>()
                .OrderByDescending(file => file["primary"]?.GetValue<bool>() == true).Take(1) : new[] { current.Data };
            foreach (var file in files)
            {
                var name = file[provider == ResourceProvider.Modrinth ? "filename" : "fileName"]!.ToString();
                var target = GameManifest.SafePath(destination, name);
                var url = file[provider == ResourceProvider.Modrinth ? "url" : "downloadUrl"]?.ToString();
                if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("作者未允许通过第三方下载此文件，请在项目网页下载后导入。");
                var hash = provider == ResourceProvider.Modrinth ? file["hashes"]?["sha1"]?.ToString()
                    : file["hashes"]?.AsArray().FirstOrDefault(h => h?["algo"]?.GetValue<int>() == 1)?["value"]?.ToString();
                var size = file[provider == ResourceProvider.Modrinth ? "size" : "fileLength"]?.GetValue<long>();
                if (!File.Exists(target)) await new GameInstaller(client).DownloadAsync(url, target, hash, size, token);
                else
                {
                    // Verify the replacement before touching an existing resource, retaining a recoverable copy.
                    var staged = target + ".update-" + Guid.NewGuid().ToString("N");
                    string? backup = null;
                    try
                    {
                        await new GameInstaller(client).DownloadAsync(url, staged, hash, size, token);
                        token.ThrowIfCancellationRequested();
                        backup = InstanceFiles.Archive(target, Path.Combine(destination, ".pcl-trash"));
                        try { File.Move(staged, target); }
                        catch { if (!File.Exists(target)) File.Move(backup, target); throw; }
                    }
                    finally { if (File.Exists(staged)) File.Delete(staged); }
                }
                installed.Add(target);
            }
        }
    }

    public async Task<ResourceRelease?> FindUpdateAsync(string file, string gameVersion, string loader, CancellationToken token)
    {
        await using var input = File.OpenRead(file);
        var hash = Convert.ToHexString(await SHA1.HashDataAsync(input, token)).ToLowerInvariant();
        using var response = await client.PostAsJsonAsync(Modrinth + "version_file/" + hash + "/update?algorithm=sha1",
            new { loaders = loader.Length == 0 ? Array.Empty<string>() : new[] { loader }, game_versions = gameVersion.Length == 0 ? Array.Empty<string>() : new[] { gameVersion } }, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
        if (json["files"]!.AsArray().Any(f => string.Equals(f?["hashes"]?["sha1"]?.ToString(), hash, StringComparison.OrdinalIgnoreCase))) return null;
        return new(json["id"]!.ToString(), json["name"]!.ToString(), json);
    }

    public async Task<JsonNode> GetJsonAsync(string url, bool curseForge, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("PCL-CE/1.0");
        if (curseForge)
        {
            if (string.IsNullOrWhiteSpace(curseForgeKey)) throw new InvalidOperationException("请先在下载设置中填写 CurseForge API 密钥。");
            request.Headers.Add("x-api-key", curseForgeKey);
        }
        using var response = await client.SendAsync(request, token); response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) ?? throw new InvalidDataException("资源服务器返回空数据。");
    }
}
