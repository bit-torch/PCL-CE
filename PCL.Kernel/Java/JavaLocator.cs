using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PCL.Kernel.Java;

public sealed record JavaRuntime(string Path, string? Version)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Version)
        ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path) ?? Path)
        : $"Java {Version}";
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

            var version = await ProbeAsync(candidate, cancellationToken).ConfigureAwait(false);
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

            var version = await ProbeAsync(candidate, cancellationToken).ConfigureAwait(false);
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

    private static async Task<string?> ProbeAsync(string javaPath, CancellationToken cancellationToken)
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
