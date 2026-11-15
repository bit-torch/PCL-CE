using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.CrossPlatform;

public sealed record LaunchAccount(string Name, string Uuid, string AccessToken, string UserType = "msa")
{
    public static LaunchAccount Offline(string name)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{1,16}$"))
            throw new ArgumentException("离线用户名须为 1–16 位英文字母、数字或下划线。", nameof(name));
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new(name, Convert.ToHexString(bytes).ToLowerInvariant(), "0", "legacy");
    }
}

/// <summary>Builds native process arguments without shell quoting or Windows-only launch wrappers.</summary>
public sealed class GameProcess(RuntimePlatform? platform = null)
{
    private readonly RuntimePlatform _platform = platform ?? RuntimePlatform.Current;

    public ProcessStartInfo CreateStartInfo(string root, string id, string java, LaunchAccount account,
        int memoryMb, string nativesDirectory, LaunchOptions? options = null)
    {
        options ??= new LaunchOptions();
        var gameDirectory = Path.GetFullPath(options.GameDirectory ?? root);
        Directory.CreateDirectory(gameDirectory);
        var features = new Dictionary<string, bool> { ["has_custom_resolution"] = options.Width.HasValue && options.Height.HasValue };
        var quickPlay = !string.IsNullOrWhiteSpace(options.ServerAddress) && manifestQuickPlay(root, id);
        features["has_quick_playsupport"] = quickPlay; features["is_quick_play_multiplayer"] = quickPlay;
        if (memoryMb < 256) throw new ArgumentOutOfRangeException(nameof(memoryMb));
        var manifest = GameManifest.Load(root, id);
        var classpath = new List<string>();
        foreach (var library in GameManifest.Libraries(manifest, _platform))
        {
            if (library["downloads"]?["artifact"]?["path"] is { } artifact)
                classpath.Add(GameManifest.SafePath(root, "libraries/" + artifact));
            else if (library["downloads"] is null && library["name"] is { } name)
                classpath.Add(GameManifest.SafePath(root, "libraries/" + GameManifest.MavenPath(name.ToString())));
        }
        classpath.Add(GameManifest.VersionPath(root, manifest["jar"]!.ToString(), ".jar"));
        foreach (var file in classpath)
            if (!File.Exists(file)) throw new FileNotFoundException("游戏依赖缺失，请先修复文件。", file);
        var assetId = manifest["assetIndex"]?["id"]?.ToString() ?? manifest["assets"]?.ToString() ?? "legacy";
        GameManifest.ValidateId(assetId);
        var replacements = new Dictionary<string, string>
        {
            ["auth_player_name"] = account.Name, ["auth_uuid"] = account.Uuid,
            ["auth_access_token"] = account.AccessToken, ["auth_session"] = "token:" + account.AccessToken + ":" + account.Uuid,
            ["user_type"] = account.UserType, ["user_properties"] = "{}", ["profile_properties"] = "{}",
            ["version_name"] = id, ["version_type"] = manifest["type"]?.ToString() ?? "release",
            ["game_directory"] = gameDirectory, ["assets_root"] = Path.GetFullPath(Path.Combine(root, "assets")),
            ["assets_index_name"] = assetId, ["game_assets"] = Path.GetFullPath(Path.Combine(root, "assets", "virtual", assetId)),
            ["natives_directory"] = nativesDirectory, ["library_directory"] = Path.GetFullPath(Path.Combine(root, "libraries")),
            ["classpath"] = string.Join(_platform.ClasspathSeparator, classpath),
            ["classpath_separator"] = _platform.ClasspathSeparator.ToString(),
            ["launcher_name"] = "PCL-CE", ["launcher_version"] = "cross-platform",
            ["clientid"] = "", ["auth_xuid"] = "",
            ["resolution_width"] = (options.Width ?? 854).ToString(), ["resolution_height"] = (options.Height ?? 480).ToString(),
            ["quickPlayMultiplayer"] = options.ServerAddress ?? "", ["quickPlaySingleplayer"] = "", ["quickPlayRealms"] = "",
            ["quickPlayPath"] = Path.Combine(gameDirectory, "logs", "quickplay.json")
        };
        string Expand(string arg) => Regex.Replace(arg, @"\$\{([^}]+)\}", match =>
            replacements.TryGetValue(match.Groups[1].Value, out var value) ? value
                : throw new InvalidDataException($"Unsupported game argument: {match.Value}"));
        var psi = new ProcessStartInfo(java)
        {
            UseShellExecute = false, WorkingDirectory = gameDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        psi.ArgumentList.Add($"-Xmx{memoryMb}M");
        var jvm = ExpandArguments(manifest["arguments"]?["jvm"] as JsonArray, features).ToList();
        if (!jvm.Any(arg => arg.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            jvm.Add("-Djava.library.path=${natives_directory}");
        if (!jvm.Any(arg => arg is "-cp" or "-classpath" or "--class-path"))
            jvm.AddRange(["-cp", "${classpath}"]);
        if (_platform.Os == "osx" && !jvm.Contains("-XstartOnFirstThread")) jvm.Add("-XstartOnFirstThread");
        foreach (var arg in jvm.Concat(options.JvmArguments)) psi.ArgumentList.Add(Expand(arg));
        if (manifest["logging"]?["client"] is { } logging)
        {
            var configId = logging["file"]?["id"]?.ToString();
            if (configId is not null && logging["argument"] is { } logArgument)
            {
                GameManifest.ValidateId(configId);
                psi.ArgumentList.Add(logArgument.ToString().Replace("${path}",
                    GameManifest.SafePath(root, "assets/log_configs/" + configId)));
            }
        }
        psi.ArgumentList.Add(manifest["mainClass"]?.ToString() ?? throw new InvalidDataException("Missing mainClass."));
        var game = manifest["arguments"]?["game"] as JsonArray;
        foreach (var arg in game is not null && game.Count != 0
                     ? ExpandArguments(game, features) : SplitLegacyArguments(manifest["minecraftArguments"]?.ToString() ?? ""))
            psi.ArgumentList.Add(Expand(arg));
        foreach (var arg in options.GameArguments) psi.ArgumentList.Add(Expand(arg));
        if (!quickPlay && !string.IsNullOrWhiteSpace(options.ServerAddress))
        {
            var server = new Uri("minecraft://" + options.ServerAddress);
            psi.ArgumentList.Add("--server"); psi.ArgumentList.Add(server.Host);
            psi.ArgumentList.Add("--port"); psi.ArgumentList.Add((server.Port < 0 ? 25565 : server.Port).ToString());
        }
        foreach (var (key, value) in options.Environment) psi.Environment[key] = value;
        return psi;
    }

    private static bool manifestQuickPlay(string root, string id) =>
        GameManifest.Load(root, id)["arguments"]?.ToJsonString().Contains("${quickPlayMultiplayer}", StringComparison.Ordinal) == true;

    public Process Start(string root, string id, string java, LaunchAccount account, int memoryMb, Action<string>? log = null, LaunchOptions? options = null)
    {
        var manifest = GameManifest.Load(root, id);
        var nativeDir = Path.Combine(Path.GetTempPath(), "PCLCE", "natives", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nativeDir);
        try
        {
            ExtractNatives(root, manifest, nativeDir);
            MaterializeLegacyAssets(root, manifest);
            var process = new Process { StartInfo = CreateStartInfo(root, id, java, account, memoryMb, nativeDir, options), EnableRaisingEvents = true };
            void LogLine(object sender, DataReceivedEventArgs e)
            {
                if (e.Data is { } line)
                    log?.Invoke(account.AccessToken.Length > 1 ? line.Replace(account.AccessToken, "[redacted]") : line);
            }
            process.OutputDataReceived += LogLine;
            process.ErrorDataReceived += LogLine;
            process.Exited += (_, _) =>
            {
                try { Directory.Delete(nativeDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("Java process could not be started.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return process;
            }
            catch { process.Dispose(); throw; }
        }
        catch
        {
            Directory.Delete(nativeDir, true);
            throw;
        }
    }

    private IEnumerable<string> ExpandArguments(JsonArray? arguments, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (arguments is null) yield break;
        foreach (var argument in arguments)
        {
            if (argument is JsonValue value) yield return value.ToString();
            else if (argument is JsonObject item && _platform.Allows(item["rules"] as JsonArray, features))
            {
                if (item["value"] is JsonArray values)
                {
                    foreach (var entry in values) yield return entry!.ToString();
                }
                else if (item["value"] is { } scalar) yield return scalar.ToString();
            }
        }
    }

    public static IEnumerable<string> SplitLegacyArguments(string arguments)
    {
        var matches = Regex.Matches(arguments, "\"([^\"]*)\"|'([^']*)'|([^\\s]+)");
        foreach (Match match in matches)
            yield return match.Groups[1].Success ? match.Groups[1].Value
                : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
    }

    private void ExtractNatives(string root, JsonObject manifest, string destination)
    {
        foreach (var library in GameManifest.Libraries(manifest, _platform))
        {
            if (_platform.NativeClassifier(library) is not { } classifier) continue;
            var relative = library["downloads"]?["classifiers"]?[classifier]?["path"]?.ToString()
                ?? throw new InvalidDataException($"Missing native classifier: {classifier}");
            var jar = GameManifest.SafePath(root, "libraries/" + relative);
            using var archive = ZipFile.OpenRead(jar);
            var excludes = library["extract"]?["exclude"]?.AsArray().Select(item => item!.ToString()).ToArray() ?? ["META-INF/"];
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || excludes.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.Ordinal))) continue;
                var path = GameManifest.SafePath(destination, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, true);
            }
        }
    }

    private static void MaterializeLegacyAssets(string root, JsonObject manifest)
    {
        if (manifest["assetIndex"]?["id"] is not { } id) return;
        var index = JsonNode.Parse(File.ReadAllText(GameManifest.SafePath(root, $"assets/indexes/{id}.json")))!;
        var virtualAssets = index["virtual"]?.GetValue<bool>() == true;
        var mapToResources = index["map_to_resources"]?.GetValue<bool>() == true;
        if (!virtualAssets && !mapToResources) return;
        foreach (var (name, asset) in index["objects"]!.AsObject())
        {
            var hash = asset!["hash"]!.ToString();
            if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid asset hash.");
            var source = GameManifest.SafePath(root, $"assets/objects/{hash[..2]}/{hash}");
            foreach (var folder in new[] { virtualAssets ? $"assets/virtual/{id}" : null, mapToResources ? "resources" : null })
            {
                if (folder is null) continue;
                var target = GameManifest.SafePath(root, folder + "/" + name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
            }
        }
    }
}
