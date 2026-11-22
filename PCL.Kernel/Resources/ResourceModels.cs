namespace PCL.Kernel.Resources;

/// <summary>资源类型。决定下载后写入的游戏目录子文件夹。</summary>
public enum ResourceType
{
    Mod,
    ResourcePack,
    Shader,
    DataPack
}

/// <summary>来自资源站点的项目摘要。</summary>
public sealed record ResourceProject(
    string Id,
    string Slug,
    string Title,
    string Description,
    string Author,
    string? IconUrl,
    long Downloads,
    string ProjectType,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Versions)
{
    /// <summary>用于下拉框与列表的显示文本。</summary>
    public string DisplayName => Title;

    public string AuthorLabel => $"by {Author}";

    public string DownloadsLabel => Downloads >= 1_000_000
        ? $"{Downloads / 1_000_000d:0.#}M"
        : Downloads >= 1_000
            ? $"{Downloads / 1_000d:0.#}K"
            : Downloads.ToString();
}

/// <summary>项目版本附带的文件。</summary>
public sealed record ResourceFile(
    string Url,
    string FileName,
    bool Primary,
    long Size,
    string? Sha1);

/// <summary>版本声明的依赖关系。Kind 对应 required / optional / incompatible / embedded。</summary>
public sealed record ResourceDependency(string? VersionId, string? ProjectId, string Kind)
{
    public bool IsRequired =>
        string.Equals(Kind, "required", StringComparison.OrdinalIgnoreCase);
}

/// <summary>项目的一个具体版本。</summary>
public sealed record ResourceVersion(
    string Id,
    string ProjectId,
    string Name,
    string VersionNumber,
    string VersionType,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<ResourceFile> Files,
    IReadOnlyList<ResourceDependency> Dependencies)
{
    public ResourceFile? PrimaryFile =>
        Files.FirstOrDefault(file => file.Primary) ?? Files.FirstOrDefault();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(VersionNumber) ? Name : $"{Name} · {VersionNumber}";
}

public sealed record ResourceSearchResult(
    IReadOnlyList<ResourceProject> Projects,
    int TotalHits);

public sealed record ResourceInstallResult(
    IReadOnlyList<string> Files,
    string Directory);
