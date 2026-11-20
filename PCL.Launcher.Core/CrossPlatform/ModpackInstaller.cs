using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace PCL.CrossPlatform;

public sealed record ModpackInstallResult(string Root, string VersionId);

/// <summary>Transactional pack import. Nothing is published until all required downloads and loaders succeed.</summary>
public sealed class ModpackInstaller(HttpClient client, string? curseForgeKey = null)
{
    public async Task<ModpackInstallResult> InstallAsync(string archive, string destination, string java, bool optionalFiles,
        IProgress<InstallProgress>? progress, CancellationToken token)
    {
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("请选择不存在的新实例目录。");
        var stage = destination + ".install-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            JsonObject? manifest = null;
            string? minecraft = null;
            LoaderVersion? loader = null;
            var resources = new ResourceCatalog(client, curseForgeKey);
            if (zip.GetEntry("modrinth.index.json") is { } index)
            {
                manifest = await ReadJson(index, token);
                if (manifest["formatVersion"]?.GetValue<int>() != 1 || manifest["game"]?.ToString() != "minecraft") throw new InvalidDataException("不支持此整合包格式版本。");
                minecraft = manifest["dependencies"]?["minecraft"]?.ToString() ?? throw new InvalidDataException("整合包未指定 Minecraft 版本。");
                foreach (var file in manifest["files"]!.AsArray().OfType<JsonObject>())
                {
                    var environment = file["env"]?["client"]?.ToString();
                    if (environment == "unsupported" || (environment == "optional" && !optionalFiles)) continue;
                    var path = GameManifest.SafePath(stage, file["path"]!.ToString());
                    var urls = file["downloads"]!.AsArray().Select(item => item!.ToString()).ToArray();
                    Exception? failure = null;
                    foreach (var url in urls)
                    {
                        try { await new GameInstaller(client).DownloadAsync(url, path, file["hashes"]?["sha1"]?.ToString(), file["fileSize"]?.GetValue<long>(), token); failure = null; break; }
                        catch (Exception ex) when (ex is HttpRequestException or IOException) { failure = ex; }
                    }
                    if (urls.Length == 0 || failure is not null) throw new IOException("整合包文件下载失败：" + file["path"], failure);
                }
                foreach (var (key, value) in manifest["dependencies"]!.AsObject())
                {
                    var kind = key switch { "fabric-loader" => LoaderKind.Fabric, "forge" => LoaderKind.Forge, "neoforge" => LoaderKind.NeoForge, _ => (LoaderKind?)null };
                    if (kind is not null) loader = await ResolveLoader(kind.Value, minecraft, value!.ToString(), token);
                }
                await ExtractOverrides(archive, stage, "overrides/", token);
                await ExtractOverrides(archive, stage, "client-overrides/", token);
            }
            else if (zip.GetEntry("manifest.json") is { } curseManifest && !await HasMcbbsAddons(curseManifest, token))
            {
                manifest = await ReadJson(curseManifest, token);
                minecraft = manifest["minecraft"]?["version"]?.ToString() ?? throw new InvalidDataException("整合包未指定 Minecraft 版本。");
                foreach (var file in manifest["files"]!.AsArray().OfType<JsonObject>())
                {
                    if (file["required"]?.GetValue<bool>() == false && !optionalFiles) continue;
                    var json = (await resources.GetJsonAsync($"https://api.curseforge.com/v1/mods/{file["projectID"]}/files/{file["fileID"]}", true, token))["data"]!.AsObject();
                    await resources.InstallAsync(ResourceProvider.CurseForge, new(json["id"]!.ToString(), json["displayName"]!.ToString(), json), Path.Combine(stage, "mods"), minecraft, "", token);
                }
                var spec = manifest["minecraft"]?["modLoaders"]?.AsArray().OfType<JsonObject>().OrderByDescending(item => item["primary"]?.GetValue<bool>() == true).FirstOrDefault()?["id"]?.ToString();
                if (spec is not null)
                {
                    var parts = spec.Split('-', 2);
                    var kind = parts[0] switch { "forge" => LoaderKind.Forge, "fabric" => LoaderKind.Fabric, "neoforge" => LoaderKind.NeoForge, _ => throw new InvalidDataException("未知加载器：" + spec) };
                    loader = await ResolveLoader(kind, minecraft, parts[1], token);
                }
                var overrides = manifest["overrides"]?.ToString() ?? "overrides";
                GameManifest.ValidateId(overrides);
                await ExtractOverrides(archive, stage, overrides + "/", token);
            }
            else if (zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("mmc-pack.json", StringComparison.Ordinal)) is { } mmc)
            {
                manifest = await ReadJson(mmc, token);
                var prefix = mmc.FullName[..^"mmc-pack.json".Length];
                var components = manifest["components"]!.AsArray().OfType<JsonObject>().ToArray();
                minecraft = components.First(c => c["uid"]?.ToString() == "net.minecraft")["version"]!.ToString();
                foreach (var component in components)
                {
                    var kind = component["uid"]?.ToString() switch
                    {
                        "net.fabricmc.fabric-loader" => LoaderKind.Fabric, "net.minecraftforge" => LoaderKind.Forge,
                        "net.neoforged" => LoaderKind.NeoForge, "com.mumfrey.liteloader" => LoaderKind.LiteLoader,
                        _ => (LoaderKind?)null
                    };
                    if (kind is not null) loader = await ResolveLoader(kind.Value, minecraft, component["version"]!.ToString(), token);
                    else if (component["uid"]?.ToString() is not ("net.minecraft" or "org.lwjgl" or "org.lwjgl3") &&
                        !zip.Entries.Any(e => e.FullName == prefix + "patches/" + component["uid"] + ".json"))
                        throw new InvalidDataException("无法安装 MultiMC 组件：" + component["uid"]);
                }
                var gamePrefix = zip.Entries.Any(e => e.FullName.StartsWith(prefix + ".minecraft/", StringComparison.Ordinal)) ? prefix + ".minecraft/" : prefix + "minecraft/";
                await ExtractOverrides(archive, stage, gamePrefix, token);
                var patchList = new JsonArray();
                foreach (var patch in zip.Entries.Where(e => e.FullName.StartsWith(prefix + "patches/", StringComparison.Ordinal) && e.FullName.EndsWith(".json")))
                    patchList.Add(await ReadJson(patch, token));
                manifest["portablePatches"] = patchList;
            }
            else if (zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("mcbbs.packmeta", StringComparison.Ordinal) || e.FullName == "manifest.json") is { } mcbbs)
            {
                manifest = await ReadJson(mcbbs, token);
                var addons = manifest["addons"]!.AsArray().OfType<JsonObject>()
                    .ToDictionary(a => a["id"]!.ToString(), a => a["version"]!.ToString());
                minecraft = addons.GetValueOrDefault("game") ?? throw new InvalidDataException("MCBBS 整合包未指定游戏版本。");
                foreach (var (key, value) in addons)
                {
                    var kind = key switch { "forge" => LoaderKind.Forge, "fabric" => LoaderKind.Fabric, "neoforge" => LoaderKind.NeoForge, "optifine" => LoaderKind.OptiFine, _ => (LoaderKind?)null };
                    if (kind is not null) loader = await ResolveLoader(kind.Value, minecraft, value, token);
                    else if (key != "game") throw new InvalidDataException("无法安装 MCBBS 加载器：" + key);
                }
                var prefix = mcbbs.FullName[..^Path.GetFileName(mcbbs.FullName).Length];
                await ExtractOverrides(archive, stage, prefix + "overrides/", token);
            }
            else if (zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("modpack.json", StringComparison.Ordinal)) is { } hmcl)
            {
                manifest = await ReadJson(hmcl, token);
                minecraft = manifest["gameVersion"]!.ToString();
                var prefix = hmcl.FullName[..^"modpack.json".Length];
                await ExtractOverrides(archive, stage, prefix + "minecraft/", token);
            }
            else
            {
                // Plain archives carry a complete .minecraft directory or local version JSON.
                var version = zip.Entries.FirstOrDefault(entry => entry.FullName.Contains("versions/", StringComparison.Ordinal) && entry.FullName.EndsWith(".json", StringComparison.Ordinal));
                if (version is null) throw new InvalidDataException("未识别整合包结构，缺少版本文件或支持的清单。");
                var prefix = version.FullName[..version.FullName.IndexOf("versions/", StringComparison.Ordinal)];
                await InstanceFiles.ExtractAsync(archive, stage, prefix, token);
                var ids = Directory.GetDirectories(Path.Combine(stage, "versions")).Select(Path.GetFileName).OfType<string>().ToArray();
                var id = ids.FirstOrDefault(value => File.Exists(GameManifest.VersionPath(stage, value))) ?? throw new InvalidDataException("整合包中没有可用版本。");
                await new GameInstaller(client).RepairAsync(stage, id, progress, token);
                Directory.Move(stage, destination); return new(destination, id);
            }
            var baseVersion = (await new GameInstaller(client).GetVersionsAsync(token)).First(v => v.Id == minecraft);
            await new GameInstaller(client).InstallAsync(stage, baseVersion, progress, token);
            var versionId = loader is null ? minecraft! : await new LoaderInstaller(client).InstallAsync(stage, java, loader, progress, token);
            if (manifest?["portablePatches"] is JsonArray patches && patches.Count > 0)
            {
                var combined = new JsonObject { ["id"] = "pack-custom", ["inheritsFrom"] = versionId, ["libraries"] = new JsonArray(),
                    ["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() } };
                foreach (var patch in patches.OfType<JsonObject>().OrderBy(p => p["order"]?.GetValue<int>() ?? 0))
                {
                    foreach (var key in new[] { "mainClass", "assetIndex", "javaVersion", "minecraftArguments" })
                        if (patch[key] is { } value) combined[key] = value.DeepClone();
                    foreach (var key in new[] { "libraries", "+libraries" })
                        foreach (var library in patch[key]?.AsArray() ?? []) combined["libraries"]!.AsArray().Add(library?.DeepClone());
                    foreach (var argument in patch["+jvmArgs"]?.AsArray() ?? []) combined["arguments"]!["jvm"]!.AsArray().Add(argument?.DeepClone());
                    foreach (var tweaker in patch["+tweakers"]?.AsArray() ?? []) { combined["arguments"]!["game"]!.AsArray().Add("--tweakClass"); combined["arguments"]!["game"]!.AsArray().Add(tweaker?.DeepClone()); }
                }
                var path = GameManifest.VersionPath(stage, "pack-custom"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, combined.ToJsonString(), token); versionId = "pack-custom";
                await new GameInstaller(client).RepairAsync(stage, versionId, progress, token);
            }
            token.ThrowIfCancellationRequested(); Directory.Move(stage, destination);
            return new(destination, versionId);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static async Task<bool> HasMcbbsAddons(ZipArchiveEntry entry, CancellationToken token) => (await ReadJson(entry, token))["addons"] is not null;

    private async Task<LoaderVersion> ResolveLoader(LoaderKind kind, string minecraft, string version, CancellationToken token)
    {
        var versions = await new LoaderInstaller(client).GetVersionsAsync(kind, minecraft, token);
        return versions.FirstOrDefault(item => item.Version == version || item.Version == minecraft + "-" + version)
            ?? throw new InvalidOperationException($"未找到整合包指定的 {kind} {version}。");
    }
    private static async Task<JsonObject> ReadJson(ZipArchiveEntry entry, CancellationToken token)
    {
        using var reader = new StreamReader(entry.Open());
        return JsonNode.Parse(await reader.ReadToEndAsync(token))!.AsObject();
    }
    private static async Task ExtractOverrides(string archive, string destination, string prefix, CancellationToken token)
    {
        // Overrides may intentionally replace downloaded files; stage them independently before merging.
        var temporary = destination + ".overrides-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);
        try
        {
            await InstanceFiles.ExtractAsync(archive, temporary, prefix, token);
            foreach (var source in InstanceFiles.EnumerateSafeFiles(temporary))
            {
                var target = GameManifest.SafePath(destination, Path.GetRelativePath(temporary, source).Replace(Path.DirectorySeparatorChar, '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(source, target, true);
            }
        }
        finally { Directory.Delete(temporary, true); }
    }
}
