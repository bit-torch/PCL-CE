namespace PCL.Kernel.Modpacks;

/// <summary>Modrinth 整合包清单中的一个待下载文件。</summary>
public sealed record ModpackFile(
    string Path,
    string DownloadUrl,
    string? Sha1,
    string? Sha512,
    long Size,
    string ClientEnvironment)
{
    /// <summary>客户端不需要该文件（例如仅服务端使用的模组）。</summary>
    public bool SkipOnClient =>
        string.Equals(ClientEnvironment, "unsupported", StringComparison.OrdinalIgnoreCase);
}

/// <summary>从 modrinth.index.json 解析出的整合包描述。</summary>
public sealed record ModpackManifest(
    string Name,
    string VersionId,
    string? Summary,
    string? MinecraftVersion,
    IReadOnlyDictionary<string, string> Dependencies,
    IReadOnlyList<ModpackFile> Files);

/// <summary>整合包安装结果。Files 与 Overrides 均为相对游戏目录的路径。</summary>
public sealed record ModpackInstallResult(
    string Name,
    string? MinecraftVersion,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Overrides);
