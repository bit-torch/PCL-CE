using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Net;

namespace PCL.CrossPlatform;

public enum LoaderKind { Fabric, LegacyFabric, Forge, NeoForge, Cleanroom, LiteLoader, OptiFine, LabyMod }
public sealed record LoaderVersion(LoaderKind Kind, string Version, string Minecraft, string Url)
{
    public override string ToString() => $"{Kind} {Version}";
}

public sealed class LoaderInstaller(HttpClient client)
{
    public async Task<IReadOnlyList<LoaderVersion>> GetVersionsAsync(LoaderKind kind, string minecraft, CancellationToken token)
    {
        GameManifest.ValidateId(minecraft);
        var game = Uri.EscapeDataString(minecraft);
        if (kind is LoaderKind.Fabric or LoaderKind.LegacyFabric)
        {
            var host = kind == LoaderKind.Fabric ? "https://meta.fabricmc.net" : "https://meta.legacyfabric.net";
            var json = JsonNode.Parse(await client.GetStringAsync($"{host}/v2/versions/loader/{game}", token))!.AsArray();
            return json.Select(item => item!["loader"]!["version"]!.ToString()).Distinct()
                .Select(version => new LoaderVersion(kind, version, minecraft, $"{host}/v2/versions/loader/{game}/{Uri.EscapeDataString(version)}/profile/json")).ToArray();
        }
        if (kind is LoaderKind.Forge or LoaderKind.NeoForge)
        {
            var maven = kind == LoaderKind.Forge ? "https://maven.minecraftforge.net/net/minecraftforge/forge"
                : "https://maven.neoforged.net/releases/net/neoforged/neoforge";
            var metadata = XDocument.Parse(await client.GetStringAsync(maven + "/maven-metadata.xml", token));
            return metadata.Descendants("version").Select(v => v.Value).Where(version => kind == LoaderKind.Forge
                    ? version.StartsWith(minecraft + "-", StringComparison.Ordinal)
                    : NeoForgeMinecraft(version) == minecraft)
                .Reverse().Select(version => new LoaderVersion(kind, version, minecraft,
                    $"{maven}/{version}/{(kind == LoaderKind.Forge ? "forge" : "neoforge")}-{version}-installer.jar")).ToArray();
        }
        if (kind == LoaderKind.Cleanroom)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/CleanroomMC/Cleanroom/releases");
            request.Headers.UserAgent.ParseAdd("PCL-CE/1.0");
            using var response = await client.SendAsync(request, token); response.EnsureSuccessStatusCode();
            var releases = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsArray();
            return releases.SelectMany(release => release!["assets"]!.AsArray()
                .Where(asset => asset!["name"]!.ToString().EndsWith("-installer.jar"))
                .Select(asset => new LoaderVersion(kind, release["tag_name"]!.ToString(), "1.12.2", asset!["browser_download_url"]!.ToString())))
                .Where(v => v.Minecraft == minecraft).ToArray();
        }
        if (kind == LoaderKind.LiteLoader)
        {
            var metadata = JsonNode.Parse(await client.GetStringAsync("https://dl.liteloader.com/versions/versions.json", token))!;
            var entry = (metadata["versions"]?[minecraft]?["artefacts"] ?? metadata["versions"]?[minecraft]?["snapshots"])?["com.mumfrey:liteloader"]?["latest"];
            return entry is null ? [] : [new(kind, entry["version"]!.ToString(), minecraft, "https://dl.liteloader.com/versions/versions.json")];
        }
        if (kind == LoaderKind.OptiFine)
        {
            var html = await client.GetStringAsync("https://optifine.net/downloads", token);
            return Regex.Matches(html, @"adloadx?\?f=(OptiFine_[^&""'<>]+\.jar)")
                .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).Distinct()
                .Where(name => name.StartsWith("OptiFine_" + minecraft + "_", StringComparison.Ordinal))
                .Select(name => new LoaderVersion(kind, name[..^4], minecraft, "https://optifine.net/adloadx?f=" + Uri.EscapeDataString(name))).ToArray();
        }
        if (kind == LoaderKind.LabyMod)
        {
            var manifest = JsonNode.Parse(await client.GetStringAsync("https://releases.r2.labymod.net/api/v1/manifest/production/latest.json", token))!;
            var commit = manifest["commitReference"]?.ToString() ?? manifest["commit_reference"]?.ToString()
                ?? throw new InvalidDataException("LabyMod 未返回版本标识。");
            var supported = manifest["minecraftVersions"]?.AsArray().FirstOrDefault(v => v?["version"]?.ToString() == minecraft);
            if (supported is null) return [];
            var url = supported["customManifestUrl"]?.ToString()
                ?? $"https://releases.r2.labymod.net/api/v1/download/manifest/labymod4/production/{game}/{commit}.json";
            return [new(kind, manifest["labyModVersion"]?.ToString() ?? commit, minecraft, url)];
        }
        throw new InvalidOperationException("请选择已下载的安装器进行本地安装。");
    }

    internal static string NeoForgeMinecraft(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 2) return "";
        return "1." + parts[0] + (parts[1] == "0" ? "" : "." + parts[1]);
    }

    public async Task<string> InstallAsync(string root, string java, LoaderVersion version, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var installer = new GameInstaller(client);
        if (!File.Exists(GameManifest.VersionPath(root, version.Minecraft)))
        {
            var baseVersion = (await installer.GetVersionsAsync(token)).FirstOrDefault(v => v.Id == version.Minecraft)
                ?? throw new InvalidOperationException("未找到对应的 Minecraft 版本。");
            await installer.InstallAsync(root, baseVersion, progress, token);
        }
        if (version.Kind is LoaderKind.Fabric or LoaderKind.LegacyFabric or LoaderKind.LabyMod)
        {
            var json = JsonNode.Parse(await client.GetStringAsync(version.Url, token))!.AsObject();
            var id = json["id"]!.ToString(); GameManifest.ValidateId(id);
            var path = GameManifest.VersionPath(root, id);
            if (File.Exists(path)) throw new IOException("该加载器版本已经安装。");
            json["inheritsFrom"] ??= version.Minecraft;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json.ToJsonString(), token);
            try { await installer.RepairAsync(root, id, progress, token); }
            catch { Directory.Delete(Path.GetDirectoryName(path)!, true); throw; }
            return id;
        }
        if (version.Kind == LoaderKind.LiteLoader)
        {
            var metadata = JsonNode.Parse(await client.GetStringAsync(version.Url, token))!;
            var entry = (metadata["versions"]?[version.Minecraft]?["artefacts"] ?? metadata["versions"]?[version.Minecraft]?["snapshots"])!["com.mumfrey:liteloader"]!["latest"]!;
            var id = "LiteLoader-" + version.Minecraft;
            var profile = new JsonObject { ["id"] = id, ["inheritsFrom"] = version.Minecraft, ["mainClass"] = "net.minecraft.launchwrapper.Launch",
                ["libraries"] = entry["libraries"]?.DeepClone() ?? new JsonArray(), ["arguments"] = new JsonObject { ["game"] = new JsonArray("--tweakClass", entry["tweakClass"]!.ToString()) } };
            profile["libraries"]!.AsArray().Add(new JsonObject { ["name"] = "com.mumfrey:liteloader:" + entry["version"], ["url"] = "https://dl.liteloader.com/versions/" });
            var path = GameManifest.VersionPath(root, id); if (File.Exists(path)) throw new IOException("该版本已经安装。");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, profile.ToJsonString(), token);
            try { await installer.RepairAsync(root, id, progress, token); return id; } catch { Directory.Delete(Path.GetDirectoryName(path)!, true); throw; }
        }
        var cache = Path.Combine(Path.GetTempPath(), "PCLCE", "installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            var jar = Path.Combine(cache, "installer.jar");
            var downloadUrl = version.Url;
            if (version.Kind == LoaderKind.OptiFine)
            {
                var html = await client.GetStringAsync(version.Url, token);
                var match = Regex.Match(html, @"downloadx?\?f=[^""'<> ]+");
                if (!match.Success) throw new InvalidDataException("OptiFine 未返回下载链接。");
                downloadUrl = "https://optifine.net/" + WebUtility.HtmlDecode(match.Value);
            }
            await installer.DownloadAsync(downloadUrl, jar, null, null, token);
            return await InstallLocalAsync(root, java, jar, version.Minecraft, progress, token);
        }
        finally { Directory.Delete(cache, true); }
    }

    public async Task<string> InstallLocalAsync(string root, string java, string jar, string minecraft,
        IProgress<InstallProgress>? progress, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(jar);
        if (zip.GetEntry("install_profile.json") is { } profileEntry)
        {
            using var reader = new StreamReader(profileEntry.Open());
            var profile = JsonNode.Parse(await reader.ReadToEndAsync(token))!.AsObject();
            JsonObject version;
            if (zip.GetEntry("version.json") is { } versionEntry)
            {
                using var versionReader = new StreamReader(versionEntry.Open());
                version = JsonNode.Parse(await versionReader.ReadToEndAsync(token))!.AsObject();
            }
            else version = profile["versionInfo"]?.DeepClone().AsObject() ?? throw new InvalidDataException("安装器缺少版本描述。");
            var id = version["id"]!.ToString(); GameManifest.ValidateId(id);
            var target = GameManifest.VersionPath(root, id);
            if (File.Exists(target)) throw new IOException("同名版本已存在。");
            foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("maven/", StringComparison.Ordinal) && !entry.FullName.EndsWith('/')))
            {
                var path = GameManifest.SafePath(root, "libraries/" + entry.FullName[6..]);
                InstanceFiles.EnsureNoLinks(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) entry.ExtractToFile(path);
            }
            var downloads = new GameInstaller(client);
            foreach (var library in (profile["libraries"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                var artifact = library["downloads"]?["artifact"];
                if (artifact?["url"]?.ToString() is { Length: > 0 } url)
                    await downloads.DownloadAsync(url, GameManifest.SafePath(root, "libraries/" + artifact["path"]), artifact["sha1"]?.ToString(), artifact["size"]?.GetValue<long>(), token);
            }
            var work = Path.Combine(Path.GetTempPath(), "PCLCE", "loader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var helper = Path.Combine(work, "forge-helper.jar");
                await using (var resource = typeof(LoaderInstaller).Assembly.GetManifestResourceStream("PCL.ForgeInstallerHelper")!)
                await using (var file = File.Create(helper)) await resource.CopyToAsync(file, token);
                var profiles = Path.Combine(root, "launcher_profiles.json");
                if (!File.Exists(profiles)) await File.WriteAllTextAsync(profiles, "{\"profiles\":{}}", token);
                progress?.Report(new("正在运行加载器安装器", 0, 1));
                await RunJavaAsync(java, ["-cp", string.Join(Path.PathSeparator, helper, Path.GetFullPath(jar)),
                    "com.bangbang93.ForgeInstaller", Path.GetFullPath(root)], root, token);
                if (!File.Exists(target))
                {
                    // Legacy Forge installers carry their complete launch profile in versionInfo.
                    if (profile["versionInfo"] is null) throw new InvalidOperationException("加载器未生成版本文件。");
                    version["inheritsFrom"] ??= minecraft;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllTextAsync(target, version.ToJsonString(), token);
                }
                await downloads.RepairAsync(root, id, progress, token);
                return id;
            }
            catch { if (Directory.Exists(Path.GetDirectoryName(target))) Directory.Delete(Path.GetDirectoryName(target)!, true); throw; }
            finally { Directory.Delete(work, true); }
        }
        if (zip.GetEntry("optifine/Installer.class") is not null)
        {
            var work = Path.Combine(Path.GetTempPath(), "PCLCE", "optifine-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            try
            {
                var compiler = Path.Combine(Path.GetDirectoryName(java)!, OperatingSystem.IsWindows() ? "javac.exe" : "javac");
                if (!File.Exists(compiler)) throw new InvalidOperationException("OptiFine 安装需要 JDK，请在 Java 页面下载对应版本的 JDK。");
                var source = Path.Combine(work, "PclOptiFineBridge.java");
                await File.WriteAllTextAsync(source, "public class PclOptiFineBridge { public static void main(String[] args) throws Exception { java.lang.reflect.Method m = Class.forName(\"optifine.Installer\").getDeclaredMethod(\"doInstall\", java.io.File.class); m.setAccessible(true); m.invoke(null, new java.io.File(args[0])); } }", token);
                var previous = Directory.GetDirectories(Path.Combine(root, "versions")).ToHashSet();
                await RunJavaAsync(compiler, ["-source", "8", "-target", "8", "-d", work, source], root, token);
                await RunJavaAsync(java, ["-cp", string.Join(Path.PathSeparator, work, Path.GetFullPath(jar)), "PclOptiFineBridge", Path.GetFullPath(root)], root, token);
                var added = Directory.GetDirectories(Path.Combine(root, "versions")).FirstOrDefault(path => !previous.Contains(path))
                    ?? throw new InvalidOperationException("OptiFine 没有生成新版本，可能已经安装。");
                var id = Path.GetFileName(added); await new GameInstaller(client).RepairAsync(root, id, progress, token); return id;
            }
            finally { Directory.Delete(work, true); }
        }
        throw new InvalidDataException("未识别的加载器安装器。");
    }

    public static async Task RunJavaAsync(string java, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken token)
    {
        var info = new ProcessStartInfo(java) { WorkingDirectory = Path.GetFullPath(workingDirectory), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token);
            var log = await output; var errors = await error;
            if (process.ExitCode != 0 || log.TrimEnd().EndsWith("false", StringComparison.Ordinal))
                throw new InvalidOperationException($"安装器返回 {process.ExitCode}：{errors[^Math.Min(errors.Length, 2000)..]} {log[^Math.Min(log.Length, 2000)..]}");
        }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
    }
}
