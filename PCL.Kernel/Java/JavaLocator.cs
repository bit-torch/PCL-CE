using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PCL.Kernel.Java;

public sealed record JavaRuntime(string Path, string? Version)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Version)
        ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path) ?? Path)
        : $"Java {Version}";

    /// <summary>主版本号；无法解析时返回 null。</summary>
    public int? MajorVersion => ParseMajorVersion(Version);

    public static int? ParseMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var text = version.Trim();

        // 旧版写法：1.8.0_392 / 1.7.0_80。
        if (text.StartsWith("1.", StringComparison.Ordinal))
        {
            var rest = text[2..];
            var separator = rest.IndexOfAny(['.', '_', '-']);
            var legacy = separator >= 0 ? rest[..separator] : rest;
            return int.TryParse(legacy, out var value) ? value : null;
        }

        // 新版写法：17.0.9 / 21 / 21-ea。
        var head = text.Split('.', '_', '-')[0];
        return int.TryParse(head, out var major) ? major : null;
    }
}

public static partial class JavaLocator
{
    [GeneratedRegex("version \"([^\"]+)\"")]
    private static partial Regex VersionRegex();

    public static async Task<JavaRuntime?> FindAsync(CancellationToken cancellationToken = default)
    {
        foreach (var candidate in EnumerateCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
                continue;

            var version = await ProbeVersionAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (version is not null)
                return new JavaRuntime(candidate, version);
        }

        return null;
    }

    /// <summary>枚举系统中所有可用的 Java 运行时，按版本去重。</summary>
    public static async Task<IReadOnlyList<JavaRuntime>> FindAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<JavaRuntime>();
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(candidate))
                continue;

            var version = await ProbeVersionAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (version is null || !versions.Add(version))
                continue;

            results.Add(new JavaRuntime(candidate, version));
        }

        return results;
    }

    public static IEnumerable<string> EnumerateCandidates()
    {
        var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            yield return Path.Combine(javaHome, "bin", executable);

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var directory in pathVariable.Split(
                         Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                yield return Path.Combine(directory.Trim(), executable);
        }

        if (OperatingSystem.IsWindows())
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BellSoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java")
            };

            foreach (var path in EnumerateUnder(roots, executable, 4))
                yield return path;
        }
        else if (OperatingSystem.IsMacOS())
        {
            var roots = new[]
            {
                "/Library/Java/JavaVirtualMachines",
                "/System/Library/Java/JavaVirtualMachines",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Java", "JavaVirtualMachines")
            };

            foreach (var path in EnumerateUnder(roots, executable, 5))
                yield return path;
        }
        else
        {
            var roots = new[]
            {
                "/usr/lib/jvm",
                "/usr/java",
                "/opt/java",
                "/opt/jdk",
                "/usr/local/lib/jvm"
            };

            foreach (var path in EnumerateUnder(roots, executable, 4))
                yield return path;

            yield return "/usr/bin/java";
            yield return "/usr/local/bin/java";
        }
    }

    private static IEnumerable<string> EnumerateUnder(
        IEnumerable<string> roots,
        string executable,
        int maxDepth)
    {
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var path in EnumerateUnder(root, executable, maxDepth))
                yield return path;
        }
    }

    private static IEnumerable<string> EnumerateUnder(string root, string executable, int maxDepth)
    {
        if (maxDepth < 0)
            yield break;

        string[] children;
        try
        {
            children = Directory.GetDirectories(root);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        var direct = Path.Combine(root, "bin", executable);
        if (File.Exists(direct))
            yield return direct;

        foreach (var child in children)
        {
            foreach (var path in EnumerateUnder(child, executable, maxDepth - 1))
                yield return path;
        }
    }

    /// <summary>探测指定可执行文件，返回 Java 运行时；路径无效或无法运行时返回 null。</summary>
    public static async Task<JavaRuntime?> ProbeAsync(
        string javaPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
            return null;

        var version = await ProbeVersionAsync(javaPath, cancellationToken).ConfigureAwait(false);
        return version is null ? null : new JavaRuntime(javaPath, version);
    }

    private static async Task<string?> ProbeVersionAsync(
        string javaPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(javaPath)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-version");

            if (!process.Start())
                return null;

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);

            if (process.ExitCode != 0)
                return null;

            var match = VersionRegex().Match(output);
            return match.Success ? match.Groups[1].Value : "unknown";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
