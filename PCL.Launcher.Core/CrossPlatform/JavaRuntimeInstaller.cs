using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PCL.CrossPlatform;

public sealed class JavaRuntimeInstaller(HttpClient client)
{
    public async Task<string> InstallAsync(int major, string parent, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";
        var architecture = RuntimePlatform.Current.Architecture == "aarch64" ? "aarch64" : "x64";
        var json = JsonNode.Parse(await client.GetStringAsync($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture={architecture}&image_type=jdk&os={os}&vendor=eclipse", token))!.AsArray();
        var release = json.FirstOrDefault() ?? throw new InvalidOperationException("没有找到适合当前系统的 Java。");
        var package = release["binary"]!["package"]!;
        var checksum = package["checksum"]!.ToString();
        if (checksum.Length != 64 || !checksum.All(Uri.IsHexDigit)) throw new InvalidDataException("Java 安装包校验格式无效。");
        var target = Path.Combine(parent, "temurin-" + major + "-" + checksum[..12]);
        if (Directory.Exists(target)) return FindJava(target);
        Directory.CreateDirectory(parent);
        var stage = target + ".stage-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(stage);
        var archive = stage + (os == "windows" ? ".zip" : ".tar.gz");
        try
        {
            progress?.Report(new("正在下载 Java", 0, 1));
            await new GameInstaller(client).DownloadAsync(package["link"]!.ToString(), archive, null, package["size"]?.GetValue<long>(), token);
            await using (var input = File.OpenRead(archive))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
                if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Java 安装包校验失败。");
            }
            progress?.Report(new("正在解压 Java", 0, 1));
            if (os == "windows") await InstanceFiles.ExtractAsync(archive, stage, token: token);
            else
            {
                await using var file = File.OpenRead(archive); await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var tar = new TarReader(gzip);
                var links = new List<(string Path, string Target)>();
                while (await tar.GetNextEntryAsync(copyData: false, cancellationToken: token) is { } entry)
                {
                    var relative = entry.Name.TrimEnd('/');
                    if (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
                    if (relative.Length == 0 || relative == ".") continue;
                    var path = GameManifest.SafePath(stage, relative); InstanceFiles.EnsureNoLinks(stage, path);
                    if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
                    if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                    {
                        var link = entry.LinkName.Replace('\\', '/');
                        if (Path.IsPathRooted(link) || link.Contains(':')) throw new InvalidDataException("Java 安装包链接越界。");
                        var basis = entry.EntryType == TarEntryType.HardLink ? stage : Path.GetDirectoryName(path)!;
                        var targetPath = Path.GetFullPath(Path.Combine(basis, link));
                        _ = GameManifest.SafePath(stage, Path.GetRelativePath(stage, targetPath));
                        links.Add((path, targetPath)); continue;
                    }
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new InvalidDataException("Java 安装包包含不支持的设备文件。");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(output, token);
                    File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1FF);
                }
                // Materialize internal file aliases after extraction; never create an archive-controlled symlink.
                while (links.Count != 0)
                {
                    var resolved = false;
                    for (var i = links.Count - 1; i >= 0; i--)
                    {
                        var link = links[i]; if (!File.Exists(link.Target)) continue;
                        InstanceFiles.EnsureNoLinks(stage, link.Target); InstanceFiles.EnsureNoLinks(stage, link.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(link.Path)!);
                        File.Copy(link.Target, link.Path, false);
                        File.SetUnixFileMode(link.Path, File.GetUnixFileMode(link.Target));
                        links.RemoveAt(i); resolved = true;
                    }
                    if (!resolved) throw new InvalidDataException("Java 安装包包含循环、缺失或目录链接。");
                }
            }
            _ = FindJava(stage); token.ThrowIfCancellationRequested(); Directory.Move(stage, target);
            return FindJava(target);
        }
        finally { if (File.Exists(archive)) File.Delete(archive); if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    private static string FindJava(string directory) => Directory.EnumerateFiles(directory, OperatingSystem.IsWindows() ? "java.exe" : "java", SearchOption.AllDirectories)
        .FirstOrDefault(path => Path.GetFileName(Path.GetDirectoryName(path)) == "bin") ?? throw new InvalidDataException("Java 安装包缺少可执行文件。");
}
