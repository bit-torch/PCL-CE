using PCL.Kernel.Mojang;

namespace PCL.Kernel.Java;

/// <summary>
/// Mojang Java 运行时清单：平台 → 组件 → 条目列表。
/// </summary>
public sealed class JavaRuntimeCatalog
    : Dictionary<string, JavaRuntimeComponents>;

public sealed class JavaRuntimeComponents
    : Dictionary<string, List<JavaRuntimeEntry>>;

public sealed class JavaRuntimeEntry
{
    public Artifact? Manifest { get; set; }

    public JavaRuntimeVersion? Version { get; set; }
}

public sealed class JavaRuntimeVersion
{
    public string Name { get; set; } = "";

    public string? Released { get; set; }
}

public sealed record JavaRuntimePackage(
    string Component,
    string Platform,
    string Version,
    string ManifestUrl,
    string ManifestSha1,
    long ManifestSize);

public sealed class JavaRuntimeFileManifest
{
    public Dictionary<string, JavaRuntimeFile> Files { get; set; } = [];
}

public sealed class JavaRuntimeFile
{
    public string Type { get; set; } = "file";

    public Dictionary<string, Artifact>? Downloads { get; set; }

    public bool Executable { get; set; }

    public string? Target { get; set; }
}
