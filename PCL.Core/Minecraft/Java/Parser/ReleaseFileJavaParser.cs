using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PCL.Core.Utils;

namespace PCL.Core.Minecraft.Java.Parser;

/// <summary>Reads the JDK release file on Unix without executing discovered binaries.</summary>
public sealed class ReleaseFileJavaParser : IJavaParser
{
    public Result<JavaInstallation, Exception> Parse(string javaExePath)
    {
        try
        {
            var file = new FileInfo(javaExePath);
            if (!file.Exists) throw new FileNotFoundException("Java executable was not found.", javaExePath);
            file = file.ResolveLinkTarget(true) as FileInfo ?? file;
            var bin = file.Directory!.FullName;
            var home = file.Directory.Parent!.FullName;
            var release = Path.Combine(home, "release");
            if (!File.Exists(release) && Directory.GetParent(home) is { } parent)
                release = Path.Combine(parent.FullName, "release"); // JDK 8's jre/bin
            var properties = File.ReadLines(release)
                .Where(line => line.Contains('='))
                .Select(line => line.Split('=', 2))
                .ToDictionary(parts => parts[0], parts => parts[1].Trim().Trim('"'));
            var versionText = properties["JAVA_VERSION"];
            var match = Regex.Match(versionText, @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:[_\.](\d+))?");
            if (!match.Success) throw new FormatException($"Invalid Java release version: {versionText}");
            int Part(int n) => match.Groups[n].Success ? int.Parse(match.Groups[n].Value) : 0;
            var version = new Version(Part(1), Part(2), Part(3), Part(4));
            var arch = properties.GetValueOrDefault("OS_ARCH") switch
            {
                "x86_64" or "amd64" => MachineType.AMD64,
                "aarch64" or "arm64" => MachineType.ARM64,
                "x86" or "i386" or "i686" => MachineType.I386,
                "arm" or "arm32" => MachineType.ARM,
                var unknown => throw new PlatformNotSupportedException($"Unsupported Java architecture: {unknown}")
            };
            var vendor = properties.GetValueOrDefault("IMPLEMENTOR", "");
            var brand = vendor.Contains("Adoptium", StringComparison.OrdinalIgnoreCase) ? JavaBrandType.EclipseTemurin
                : vendor.Contains("Oracle", StringComparison.OrdinalIgnoreCase) ? JavaBrandType.Oracle
                : vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? JavaBrandType.Microsoft
                : JavaBrandType.OpenJDK;
            return Result<JavaInstallation, Exception>.Ok(new JavaInstallation(bin, version, brand, arch,
                arch is MachineType.AMD64 or MachineType.ARM64, !File.Exists(Path.Combine(bin, "javac"))));
        }
        catch (Exception ex)
        {
            return Result<JavaInstallation, Exception>.Err(ex);
        }
    }
}
