using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Kernel.Mojang;

namespace PCL.Kernel.Launch;

/// <summary>
/// 原版启动器核心：解析版本继承、构建 classpath 与参数、启动 Java 进程。
/// 不包含 PCL 的签名、统计、赞助或更新逻辑。
/// </summary>
public sealed class GameLauncher
{
    private readonly LauncherPaths _paths;

    public GameLauncher(LauncherPaths paths)
    {
        _paths = paths;
    }

    public Task<GameSession> LaunchAsync(
        LaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(options.JavaPath))
            throw new FileNotFoundException($"找不到 Java 可执行文件：{options.JavaPath}");

        var chain = new List<string>();
        var detail = LoadDetail(options.VersionId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), chain);

        var gameDirectory = Path.GetFullPath(options.GameDirectory ?? _paths.GameDirectory);
        Directory.CreateDirectory(gameDirectory);

        var nativesDirectory = _paths.NativesDirectory(options.VersionId);
        Directory.CreateDirectory(nativesDirectory);

        var classpath = BuildClasspath(detail, chain);
        var variables = BuildVariables(detail, options, gameDirectory, nativesDirectory, classpath);

        var jvmArguments = BuildJvmArguments(detail, options, nativesDirectory, classpath, variables);

        var gameArguments = ArgumentParser
            .Parse(detail.Arguments, "game", variables, detail.MinecraftArguments)
            .ToList();

        if (options.WindowWidth > 0 && options.WindowHeight > 0)
        {
            if (!gameArguments.Contains("--width"))
            {
                gameArguments.Add("--width");
                gameArguments.Add(options.WindowWidth.ToString(CultureInfo.InvariantCulture));
            }

            if (!gameArguments.Contains("--height"))
            {
                gameArguments.Add("--height");
                gameArguments.Add(options.WindowHeight.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (options.FullScreen && !gameArguments.Contains("--fullscreen"))
            gameArguments.Add("--fullscreen");

        gameArguments.AddRange(options.ExtraGameArgs);

        if (string.IsNullOrWhiteSpace(detail.MainClass))
            throw new InvalidDataException("版本 JSON 缺少 MainClass。");

        var startInfo = new ProcessStartInfo
        {
            FileName = options.JavaPath,
            WorkingDirectory = gameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in jvmArguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.ArgumentList.Add(detail.MainClass!);

        foreach (var argument in gameArguments)
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("无法启动 Java 进程。");

        return Task.FromResult(new GameSession(process));
    }

    private List<string> BuildJvmArguments(
        VersionDetail detail,
        LaunchOptions options,
        string nativesDirectory,
        string classpath,
        IReadOnlyDictionary<string, string> variables)
    {
        var arguments = new List<string>();

        var hasJvmSection =
            detail.Arguments is { ValueKind: JsonValueKind.Object } document &&
            document.TryGetProperty("jvm", out _);

        if (hasJvmSection)
            arguments.AddRange(ArgumentParser.Parse(detail.Arguments, "jvm", variables));
        else
        {
            arguments.Add($"-Djava.library.path={nativesDirectory}");
            arguments.Add("-cp");
            arguments.Add(classpath);
        }

        if (!arguments.Any(argument => argument is "-cp" or "-classpath"))
        {
            arguments.Add("-cp");
            arguments.Add(classpath);
        }

        if (!arguments.Any(argument =>
                argument.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            arguments.Add($"-Djava.library.path={nativesDirectory}");

        if (!arguments.Any(argument =>
                argument.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase)))
            arguments.Add($"-Xmx{Math.Clamp(options.MaxMemoryMb, 512, 65536)}M");

        arguments.Add("-Dfile.encoding=UTF-8");
        arguments.Add("-Dminecraft.launcher.brand=PCL-CE");
        arguments.Add("-Dminecraft.launcher.version=1.0");

        if (!string.IsNullOrWhiteSpace(options.AuthlibInjectorPath) &&
            !string.IsNullOrWhiteSpace(options.YggdrasilApiRoot))
        {
            arguments.Add($"-javaagent:{options.AuthlibInjectorPath}={options.YggdrasilApiRoot}");
            arguments.Add("-Dauthlibinjector.side=client");
        }

        arguments.AddRange(options.ExtraJvmArgs);

        return arguments;
    }

    private Dictionary<string, string> BuildVariables(
        VersionDetail detail,
        LaunchOptions options,
        string gameDirectory,
        string nativesDirectory,
        string classpath) =>
        new(StringComparer.Ordinal)
        {
            ["auth_player_name"] = options.Username,
            ["version_name"] = detail.Id,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = _paths.AssetsDirectory,
            ["assets_index_name"] = detail.Assets ?? detail.AssetIndex?.Id ?? detail.Id,
            ["auth_uuid"] = string.IsNullOrWhiteSpace(options.Uuid)
                ? OfflineAccount.CreateUuid(options.Username)
                : options.Uuid,
            ["auth_access_token"] = string.IsNullOrWhiteSpace(options.AccessToken)
                ? "0"
                : options.AccessToken,
            ["auth_session"] = string.IsNullOrWhiteSpace(options.AccessToken)
                ? "0"
                : options.AccessToken,
            ["user_type"] = string.IsNullOrWhiteSpace(options.UserType)
                ? "legacy"
                : options.UserType,
            ["user_properties"] = options.UserProperties ?? "{}",
            ["clientid"] = "pcl-ce",
            ["xuid"] = options.Xuid ?? "",
            ["version_type"] = detail.Type,
            ["natives_directory"] = nativesDirectory,
            ["launcher_name"] = "PCL-CE",
            ["launcher_version"] = "1.0",
            ["classpath"] = classpath,
            ["game_assets"] = Path.Combine(_paths.AssetsDirectory, "virtual", "legacy"),
            ["resolution_width"] = options.WindowWidth > 0
                ? options.WindowWidth.ToString(CultureInfo.InvariantCulture)
                : "854",
            ["resolution_height"] = options.WindowHeight > 0
                ? options.WindowHeight.ToString(CultureInfo.InvariantCulture)
                : "480",
            ["quickPlayPath"] = "",
            ["quickPlaySingleplayer"] = "",
            ["quickPlayMultiplayer"] = "",
            ["quickPlayRealms"] = ""
        };

    private string BuildClasspath(VersionDetail detail, IReadOnlyList<string> chain)
    {
        var entries = new List<string>();
        var missing = new List<string>();

        foreach (var versionId in chain)
        {
            var jar = _paths.VersionJarPath(versionId);
            if (File.Exists(jar))
                entries.Add(jar);
        }

        foreach (var library in detail.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(library.Rules))
                continue;

            if (library.Downloads?.Artifact is not { } artifact)
                continue;

            var relativePath = string.IsNullOrWhiteSpace(artifact.Path)
                ? MavenPath.FromCoordinate(library.Name)
                : artifact.Path;
            var fullPath = Path.Combine(
                _paths.LibrariesDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(fullPath))
                entries.Add(fullPath);
            else
                missing.Add(fullPath);
        }

        if (missing.Count > 0)
            throw new FileNotFoundException($"缺少依赖库文件，请重新安装：{missing[0]}");

        if (entries.Count == 0)
            throw new FileNotFoundException("未找到可用的客户端或依赖库，请先安装该版本。");

        return string.Join(Path.PathSeparator, entries.Distinct(StringComparer.Ordinal));
    }

    private VersionDetail LoadDetail(
        string versionId,
        ISet<string> visiting,
        ICollection<string> chain)
    {
        if (!visiting.Add(versionId))
            throw new InvalidOperationException($"版本继承存在循环：{versionId}");

        var path = _paths.VersionJsonPath(versionId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到版本 {versionId} 的 JSON：{path}");

        var detail = JsonSerializer.Deserialize<VersionDetail>(
                         File.ReadAllText(path), JsonDefaults.Options)
                     ?? throw new InvalidDataException($"无法解析版本文件：{path}");

        chain.Add(versionId);

        if (!string.IsNullOrWhiteSpace(detail.InheritsFrom))
        {
            var parent = LoadDetail(detail.InheritsFrom, visiting, chain);
            detail = Merge(parent, detail);
        }

        visiting.Remove(versionId);
        return detail;
    }

    private static VersionDetail Merge(VersionDetail parent, VersionDetail child)
    {
        var libraries = new List<Library>(parent.Libraries);
        foreach (var library in child.Libraries)
        {
            var index = libraries.FindIndex(existing =>
                string.Equals(existing.Name, library.Name, StringComparison.Ordinal));
            if (index >= 0)
                libraries[index] = library;
            else
                libraries.Add(library);
        }

        return new VersionDetail
        {
            Id = string.IsNullOrWhiteSpace(child.Id) ? parent.Id : child.Id,
            Type = string.IsNullOrWhiteSpace(child.Type) ? parent.Type : child.Type,
            MainClass = child.MainClass ?? parent.MainClass,
            Assets = child.Assets ?? parent.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Downloads = child.Downloads ?? parent.Downloads,
            JavaVersion = child.JavaVersion ?? parent.JavaVersion,
            Libraries = libraries,
            Arguments = MergeArguments(parent.Arguments, child.Arguments),
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments
        };
    }

    private static JsonElement? MergeArguments(JsonElement? parent, JsonElement? child)
    {
        if (parent is not { ValueKind: JsonValueKind.Object })
            return child;
        if (child is not { ValueKind: JsonValueKind.Object })
            return parent;

        var merged = new JsonObject();

        foreach (var section in new[] { "jvm", "game" })
        {
            var array = new JsonArray();

            foreach (var element in EnumerateSection(parent.Value, section))
                array.Add(JsonNode.Parse(element.GetRawText())!);

            foreach (var element in EnumerateSection(child.Value, section))
                array.Add(JsonNode.Parse(element.GetRawText())!);

            merged[section] = array;
        }

        return JsonSerializer.SerializeToElement(merged);
    }

    private static IEnumerable<JsonElement> EnumerateSection(JsonElement element, string section)
    {
        if (!element.TryGetProperty(section, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in array.EnumerateArray())
            yield return entry;
    }
}
