namespace PCL.Kernel.GameData;

/// <summary>一个本地存档目录。</summary>
public sealed record SaveEntry(
    string Name,
    string DirectoryPath,
    string? IconPath,
    DateTimeOffset LastModified)
{
    public string LastModifiedLabel => LastModified.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public bool HasIcon => IconPath is not null;
}

/// <summary>一张游戏内截图。</summary>
public sealed record ScreenshotEntry(
    string FileName,
    string FilePath,
    DateTimeOffset Created,
    long Size)
{
    public string CreatedLabel => Created.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string SizeLabel => Size >= 1024 * 1024
        ? $"{Size / (1024d * 1024d):0.#} MB"
        : $"{Size / 1024d:0.#} KB";
}

/// <summary>servers.dat 中的一条服务器记录。</summary>
public sealed record ServerEntry(string Name, string Ip, string? Icon)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Ip : Name;
}

/// <summary>日志分析发现的单条问题。</summary>
public sealed record LogIssue(string Source, string Title, string Hint, string Detail);

public sealed record LogAnalysisResult(
    IReadOnlyList<LogIssue> Issues,
    int FilesScanned);
