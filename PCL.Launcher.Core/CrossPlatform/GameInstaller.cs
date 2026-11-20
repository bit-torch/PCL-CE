using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PCL.CrossPlatform;

public sealed record InstallProgress(string Message, int Completed, int Total);
public sealed record AvailableVersion(string Id, string Type, string Url, string Sha1)
{
    public override string ToString() => $"{Id} · {Type}";
}

/// <summary>Installs Mojang client files with cancellation, hash validation and atomic replacement.</summary>
public sealed class GameInstaller(HttpClient client, RuntimePlatform? platform = null)
{
    private readonly RuntimePlatform _platform = platform ?? RuntimePlatform.Current;
    public const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public async Task<IReadOnlyList<AvailableVersion>> GetVersionsAsync(CancellationToken token = default)
    {
        var json = JsonNode.Parse(await client.GetStringAsync(VersionManifestUrl, token));
        return json!["versions"]!.AsArray().OfType<JsonObject>().Select(v => new AvailableVersion(
            v["id"]!.ToString(), v["type"]!.ToString(), v["url"]!.ToString(), v["sha1"]!.ToString())).ToArray();
    }

    public async Task InstallAsync(string root, AvailableVersion version, IProgress<InstallProgress>? progress = null,
        CancellationToken token = default)
    {
        await DownloadAsync(version.Url, GameManifest.VersionPath(root, version.Id), version.Sha1, null, token);
        await RepairAsync(root, version.Id, progress, token);
    }

    public async Task RepairAsync(string root, string id, IProgress<InstallProgress>? progress = null,
        CancellationToken token = default)
    {
        var manifest = GameManifest.Load(root, id);
        var files = new List<(string Url, string Path, string? Hash, long? Size)>();
        void Add(JsonNode? download, string path)
        {
            if (download?["url"] is not { } url) return;
            files.Add((url.ToString(), path, download["sha1"]?.ToString(), download["size"]?.GetValue<long>()));
        }
        Add(manifest["downloads"]?["client"], GameManifest.VersionPath(root, manifest["jar"]!.ToString(), ".jar"));
        foreach (var library in GameManifest.Libraries(manifest, _platform))
        {
            if (library["downloads"]?["artifact"] is { } artifact)
                Add(artifact, GameManifest.SafePath(root, "libraries/" + artifact["path"]));
            else if (library["downloads"] is null && library["name"] is { } name)
            {
                var path = GameManifest.MavenPath(name.ToString());
                files.Add(((library["url"]?.ToString() ?? "https://libraries.minecraft.net/").TrimEnd('/') + "/" + path,
                    GameManifest.SafePath(root, "libraries/" + path), null, null));
            }
            if (_platform.NativeClassifier(library) is { } classifier)
            {
                var native = library["downloads"]?["classifiers"]?[classifier]
                    ?? throw new PlatformNotSupportedException($"Missing {classifier} for {library["name"]}");
                Add(native, GameManifest.SafePath(root, "libraries/" + native["path"]));
            }
        }
        if (manifest["assetIndex"] is { } index)
        {
            GameManifest.ValidateId(index["id"]!.ToString());
            var indexPath = GameManifest.SafePath(root, $"assets/indexes/{index["id"]}.json");
            await DownloadAsync(index["url"]!.ToString(), indexPath, index["sha1"]?.ToString(), index["size"]?.GetValue<long>(), token);
            var assets = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, token))!["objects"]!.AsObject();
            foreach (var (_, asset) in assets)
            {
                var hash = asset!["hash"]!.ToString();
                if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid asset hash.");
                var path = $"{hash[..2]}/{hash}";
                files.Add(($"https://resources.download.minecraft.net/{path}",
                    GameManifest.SafePath(root, "assets/objects/" + path), hash, asset["size"]?.GetValue<long>()));
            }
        }
        if (manifest["logging"]?["client"]?["file"] is { } logConfig)
        {
            var configId = logConfig["id"]!.ToString();
            GameManifest.ValidateId(configId);
            Add(logConfig, GameManifest.SafePath(root, "assets/log_configs/" + configId));
        }
        var completed = 0;
        progress?.Report(new("正在检查与下载游戏文件", 0, files.Count));
        await Parallel.ForEachAsync(files.DistinctBy(file => file.Path),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token }, async (file, ct) =>
            {
                await DownloadAsync(file.Url, file.Path, file.Hash, file.Size, ct);
                progress?.Report(new("正在检查与下载游戏文件", Interlocked.Increment(ref completed), files.Count));
            });
    }

    public async Task DownloadAsync(string url, string destination, string? sha1, long? size, CancellationToken token)
    {
        if (await IsValidAsync(destination, sha1, size, token)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("Game downloads require HTTPS.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await response.Content.CopyToAsync(output, token);
            if (!await IsValidAsync(temporary, sha1, size, token))
                throw new InvalidDataException($"Downloaded file failed validation: {Path.GetFileName(destination)}");
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<bool> IsValidAsync(string path, string? sha1, long? size, CancellationToken token)
    {
        if (!File.Exists(path) || (size.HasValue && new FileInfo(path).Length != size)) return false;
        if (sha1 is null) return new FileInfo(path).Length > 0;
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA1.HashDataAsync(stream, token));
        return hash.Equals(sha1, StringComparison.OrdinalIgnoreCase);
    }
}
