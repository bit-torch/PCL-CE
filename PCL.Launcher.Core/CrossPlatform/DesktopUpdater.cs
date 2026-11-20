using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PCL.CrossPlatform;

public sealed record DesktopRelease(string Version, string Notes, string AssetName, string DownloadUrl, long Size, string? Sha256);
public sealed record PreparedUpdate(string SourceDirectory, string InstallDirectory, string ExecutableRelativePath, string WorkingDirectory);

/// <summary>Downloads and validates a platform package, then uses a small platform helper after the launcher exits.</summary>
public sealed class DesktopUpdater(HttpClient client)
{
    public static string CurrentRid => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + (RuntimePlatform.Current.Architecture == "aarch64" ? "arm64" : "x64");
    public async Task<DesktopRelease> LatestAsync(bool prerelease, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/bit-torch/PCL-CE/releases" + (prerelease ? "" : "/latest"));
        request.Headers.UserAgent.ParseAdd("PCL-CE/1.0");
        using var response = await client.SendAsync(request, token); response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!;
        var release = prerelease ? json.AsArray().FirstOrDefault(item => item?["draft"]?.GetValue<bool>() != true) : json;
        if (release is null) throw new InvalidOperationException("没有可用发行版。");
        var asset = release["assets"]!.AsArray().FirstOrDefault(item => item?["name"]?.ToString().StartsWith("PCL-CE-" + CurrentRid, StringComparison.Ordinal) == true && item["name"]!.ToString().EndsWith(".tar.gz"))
            ?? throw new InvalidOperationException("最新发行版没有当前平台的跨平台桌面包。");
        var digest = asset["digest"]?.ToString();
        return new(release["tag_name"]!.ToString(), release["body"]?.ToString() ?? "", asset["name"]!.ToString(),
            asset["browser_download_url"]!.ToString(), asset["size"]!.GetValue<long>(), digest?.StartsWith("sha256:") == true ? digest[7..] : null);
    }

    public async Task DownloadAsync(DesktopRelease release, string destination, CancellationToken token)
    {
        await new GameInstaller(client).DownloadAsync(release.DownloadUrl, destination, null, release.Size, token);
        if (release.Sha256 is not null)
        {
            await using var input = File.OpenRead(destination);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            { File.Delete(destination); throw new InvalidDataException("更新包 SHA-256 校验失败。"); }
        }
    }

    public static async Task<PreparedUpdate> PrepareAsync(string archive, string installDirectory, string stagingParent, CancellationToken token)
    {
        var stage = Path.Combine(stagingParent, "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        var payload = Path.Combine(stage, "contents"); Directory.CreateDirectory(payload);
        try
        {
            await using var file = File.OpenRead(archive); await using var gzip = new GZipStream(file, CompressionMode.Decompress); using var reader = new TarReader(gzip);
            while (await reader.GetNextEntryAsync(false, token) is { } entry)
            {
                var name = entry.Name.TrimEnd('/'); if (name.StartsWith("./")) name = name[2..]; if (name is "" or ".") continue;
                var path = GameManifest.SafePath(payload, name); InstanceFiles.EnsureNoLinks(payload, path);
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new InvalidDataException("更新包包含不支持的链接或特殊文件。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(output, token);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1FF);
            }
            var executable = OperatingSystem.IsWindows() ? "PCL-CE.exe" : "PCL-CE";
            var source = payload; var destination = Path.GetFullPath(installDirectory); var relative = executable;
            if (OperatingSystem.IsMacOS())
            {
                var bundle = Directory.GetDirectories(payload, "*.app").SingleOrDefault();
                if (bundle is not null)
                {
                    var currentBundle = new DirectoryInfo(destination).Parent?.Parent;
                    if (currentBundle?.Name.EndsWith(".app", StringComparison.Ordinal) == true)
                    { source = bundle; destination = currentBundle.FullName; relative = "Contents/MacOS/" + executable; }
                    else source = Path.Combine(bundle, "Contents", "MacOS");
                }
            }
            if (!File.Exists(GameManifest.SafePath(source, relative)) || !File.Exists(Path.Combine(source, Path.GetDirectoryName(relative) ?? "", "PCL-CE.dll")))
                throw new InvalidDataException("更新包缺少启动器程序。");
            return new(source, destination, relative, stage);
        }
        catch { Directory.Delete(stage, true); throw; }
    }

    public static ProcessStartInfo CreateApplyProcess(PreparedUpdate update, int parentPid)
    {
        if (parentPid <= 0) throw new ArgumentOutOfRangeException(nameof(parentPid));
        var windows = OperatingSystem.IsWindows();
        var script = Path.Combine(update.WorkingDirectory, windows ? "apply-update.ps1" : "apply-update.sh");
        File.WriteAllText(script, windows ? WindowsScript : UnixScript);
        var info = new ProcessStartInfo(windows ? "powershell.exe" : "/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
        if (windows) foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File" }) info.ArgumentList.Add(arg);
        info.ArgumentList.Add(script); info.ArgumentList.Add(parentPid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add(update.SourceDirectory); info.ArgumentList.Add(update.InstallDirectory);
        info.ArgumentList.Add(Path.Combine(update.WorkingDirectory, "previous")); info.ArgumentList.Add(update.ExecutableRelativePath);
        info.ArgumentList.Add(Path.Combine(update.WorkingDirectory, "update.log"));
        return info;
    }

    private const string UnixScript = """
        set -eu
        parent="$1"; stage="$2"; target="$3"; backup="$4"; executable="$5"; log="$6"
        exec >>"$log" 2>&1
        count=0
        while kill -0 "$parent" 2>/dev/null; do
          count=$((count + 1)); [ "$count" -lt 120 ] || exit 1
          sleep 1
        done
        mkdir -p "$backup"
        cp -a "$target/." "$backup/"
        if ! cp -a "$stage/." "$target/"; then cp -a "$backup/." "$target/"; exit 1; fi
        "$target/$executable" >/dev/null 2>&1 &
        """;
    private const string WindowsScript = """
        param([int]$parentPid, [string]$stage, [string]$target, [string]$backup, [string]$executable, [string]$log)
        $ErrorActionPreference = 'Stop'
        try {
          for ($i = 0; $i -lt 120 -and (Get-Process -Id $parentPid -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }
          if (Get-Process -Id $parentPid -ErrorAction SilentlyContinue) { throw 'Launcher did not exit.' }
          New-Item -ItemType Directory -Path $backup -Force | Out-Null
          Get-ChildItem -LiteralPath $target -Force | Copy-Item -Destination $backup -Recurse -Force
          try { Get-ChildItem -LiteralPath $stage -Force | Copy-Item -Destination $target -Recurse -Force }
          catch { Get-ChildItem -LiteralPath $backup -Force | Copy-Item -Destination $target -Recurse -Force; throw }
          Start-Process -FilePath (Join-Path $target $executable)
        } catch { $_ | Out-File -LiteralPath $log; exit 1 }
        """;
}
