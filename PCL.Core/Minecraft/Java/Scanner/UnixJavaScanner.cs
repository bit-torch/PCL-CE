using System;
using System.Collections.Generic;
using System.IO;

namespace PCL.Core.Minecraft.Java.Scanner;

/// <summary>Conventional Linux, macOS, SDKMAN and JAVA_HOME installations.</summary>
public sealed class UnixJavaScanner : IJavaScanner
{
    public void Scan(ICollection<string> results)
    {
        if (OperatingSystem.IsWindows()) return;
        AddHome(Environment.GetEnvironmentVariable("JAVA_HOME"));
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots = ["/usr/lib/jvm", "/usr/java", "/opt/java", "/Library/Java/JavaVirtualMachines",
            Path.Combine(user, "Library/Java/JavaVirtualMachines"), Path.Combine(user, ".sdkman/candidates/java")];
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    AddHome(dir);
                    AddHome(Path.Combine(dir, "Contents", "Home"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return;

        void AddHome(string? home)
        {
            if (string.IsNullOrWhiteSpace(home)) return;
            var executable = Path.Combine(home, "bin", "java");
            if (File.Exists(executable)) results.Add(executable);
        }
    }
}
