using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Kernel.Mojang;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class VersionManifest
{
    public LatestVersions Latest { get; set; } = new();

    public List<VersionSummary> Versions { get; set; } = [];
}

public sealed class LatestVersions
{
    public string Release { get; set; } = "";

    public string Snapshot { get; set; } = "";
}

public sealed class VersionSummary
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    public string Url { get; set; } = "";

    public string? Sha1 { get; set; }

    public long? Size { get; set; }

    public DateTimeOffset? ReleaseTime { get; set; }
}

public sealed class VersionDetail
{
    public string Id { get; set; } = "";

    public string? InheritsFrom { get; set; }

    public string Type { get; set; } = "release";

    public string? MainClass { get; set; }

    public string? Assets { get; set; }

    public AssetIndexInfo? AssetIndex { get; set; }

    public VersionDownloads? Downloads { get; set; }

    public JavaVersionInfo? JavaVersion { get; set; }

    public List<Library> Libraries { get; set; } = [];

    public JsonElement? Arguments { get; set; }

    public string? MinecraftArguments { get; set; }
}

public sealed class VersionDownloads
{
    public Artifact? Client { get; set; }

    public Artifact? Server { get; set; }

    public Artifact? ClientMappings { get; set; }

    public Artifact? ServerMappings { get; set; }
}

public sealed class Artifact
{
    public string Path { get; set; } = "";

    public string Sha1 { get; set; } = "";

    public long Size { get; set; }

    public string Url { get; set; } = "";
}

public sealed class JavaVersionInfo
{
    public string Component { get; set; } = "";

    public int MajorVersion { get; set; }
}

public sealed class AssetIndexInfo
{
    public string Id { get; set; } = "";

    public string Sha1 { get; set; } = "";

    public long Size { get; set; }

    public long TotalSize { get; set; }

    public string Url { get; set; } = "";

    public bool Virtual { get; set; }
}

public sealed class Library
{
    public string Name { get; set; } = "";

    public LibraryDownloads? Downloads { get; set; }

    public List<MojangRule>? Rules { get; set; }

    public Dictionary<string, string>? Natives { get; set; }
}

public sealed class LibraryDownloads
{
    public Artifact? Artifact { get; set; }

    public Dictionary<string, Artifact>? Classifiers { get; set; }
}

public sealed class MojangRule
{
    public string Action { get; set; } = "allow";

    public RuleOs? Os { get; set; }

    public Dictionary<string, bool>? Features { get; set; }
}

public sealed class RuleOs
{
    public string? Name { get; set; }

    public string? Arch { get; set; }

    public string? Version { get; set; }
}

public sealed class AssetIndexFile
{
    public Dictionary<string, AssetObject> Objects { get; set; } = [];
}

public sealed class AssetObject
{
    public string Hash { get; set; } = "";

    public long Size { get; set; }
}
