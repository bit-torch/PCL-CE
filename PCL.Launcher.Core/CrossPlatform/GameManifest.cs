using System.Text.Json.Nodes;

namespace PCL.CrossPlatform;

/// <summary>Version resolution for the portable host; independent of WPF and global instance state.</summary>
public static class GameManifest
{
    public static string SafePath(string root, string relative)
    {
        // Mojang paths use '/', regardless of the host. Reject Windows syntax on Unix too.
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            Path.IsPathRooted(relative) || relative.Split('/').Any(part => part is ".." or "."))
            throw new InvalidDataException($"Unsafe relative game path: {relative}");
        var fullRoot = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(fullRoot)) fullRoot += Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!result.StartsWith(fullRoot, comparison)) throw new InvalidDataException("Path escapes the game directory.");
        return result;
    }

    public static string VersionPath(string root, string id, string extension = ".json")
    {
        ValidateId(id);
        return SafePath(root, $"versions/{id}/{id}{extension}");
    }

    public static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['/', '\\', ':']) >= 0 || id is "." or "..")
            throw new InvalidDataException("Invalid Minecraft version id.");
    }

    public static JsonObject Load(string root, string id) => Load(root, id, new HashSet<string>(StringComparer.Ordinal));

    private static JsonObject Load(string root, string id, HashSet<string> visited)
    {
        if (visited.Count > 32 || !visited.Add(id)) throw new InvalidDataException("Cyclic or excessive version inheritance.");
        var child = JsonNode.Parse(File.ReadAllText(VersionPath(root, id)))?.AsObject()
            ?? throw new InvalidDataException("Empty version manifest.");
        if (child["inheritsFrom"] is not { } inherits)
        {
            child["jar"] ??= id;
            return child;
        }
        var parent = Load(root, inherits.ToString(), visited);
        var merged = parent.DeepClone().AsObject();
        foreach (var (key, value) in child)
        {
            if (key is "libraries" or "arguments" or "inheritsFrom") continue;
            merged[key] = value?.DeepClone();
        }
        var libraries = new List<JsonNode>();
        foreach (var library in (parent["libraries"]?.AsArray() ?? []).Concat(child["libraries"]?.AsArray() ?? []))
        {
            if (library is null) continue;
            var name = library["name"]?.ToString().Split(':');
            if (name is { Length: >= 2 })
                libraries.RemoveAll(old => LibraryKey(old) == LibraryKey(library));
            libraries.Add(library.DeepClone());
        }
        merged["libraries"] = new JsonArray(libraries.ToArray());
        var arguments = new JsonObject();
        foreach (var kind in new[] { "jvm", "game" })
            arguments[kind] = new JsonArray((parent["arguments"]?[kind]?.AsArray() ?? [])
                .Concat(child["arguments"]?[kind]?.AsArray() ?? []).Select(arg => arg?.DeepClone()).ToArray());
        if (parent["arguments"] is not null || child["arguments"] is not null) merged["arguments"] = arguments;
        return merged;
    }

    private static string LibraryKey(JsonNode library)
    {
        var parts = library["name"]!.ToString().Split(':');
        return string.Join(':', parts.Take(2)) + (parts.Length > 3 ? ":" + parts[3] : "");
    }

    public static IEnumerable<JsonObject> Libraries(JsonObject manifest, RuntimePlatform platform) =>
        (manifest["libraries"]?.AsArray() ?? []).OfType<JsonObject>()
        .Where(library => platform.Allows(library["rules"] as JsonArray));

    public static string MavenPath(string coordinate)
    {
        var extensionParts = coordinate.Split('@', 2);
        var parts = extensionParts[0].Split(':');
        if (parts.Length is < 3 or > 4) throw new InvalidDataException($"Invalid Maven coordinate: {coordinate}");
        var extension = extensionParts.Length == 2 ? extensionParts[1] : "jar";
        return $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}{(parts.Length == 4 ? "-" + parts[3] : "")}.{extension}";
    }
}
