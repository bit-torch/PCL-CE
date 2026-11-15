using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Minecraft.Java.Parser;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;
using PCL.CrossPlatform;

namespace PCL.CrossPlatform.Test;

[TestClass]
public class PlatformTests
{
    [TestMethod]
    public void DomainAssembliesDoNotDependOnDesktopFrameworks()
    {
        foreach (var assembly in new[] { typeof(PCL.Core.App.Basics).Assembly, typeof(LauncherCommon).Assembly })
        {
            var references = assembly.GetReferencedAssemblies().Select(value => value.Name).ToArray();
            foreach (var forbidden in new[] { "PresentationFramework", "PresentationCore", "WindowsBase", "Avalonia.Controls" })
                Assert.DoesNotContain(forbidden, references);
        }
    }

    [TestMethod]
    public void RulesUseLastMatchingActionAndPlatformFeatures()
    {
        var rules = JsonNode.Parse("""
            [{"action":"allow"},{"action":"disallow","os":{"name":"osx"}},
             {"action":"allow","os":{"name":"osx","arch":"aarch64"}}]
            """)!.AsArray();
        Assert.IsTrue(new RuntimePlatform("linux", "amd64", "6.1", ':').Allows(rules));
        Assert.IsFalse(new RuntimePlatform("osx", "amd64", "14.0", ':').Allows(rules));
        Assert.IsTrue(new RuntimePlatform("osx", "aarch64", "14.0", ':').Allows(rules));
        var feature = JsonNode.Parse("""[{"action":"allow","features":{"is_demo_user":true}}]""")!.AsArray();
        Assert.IsFalse(RuntimePlatform.Current.Allows(feature));
        Assert.IsTrue(RuntimePlatform.Current.Allows(feature, new Dictionary<string, bool> { ["is_demo_user"] = true }));
        Assert.IsFalse(RuntimePlatform.Current.Allows(new JsonArray()));
    }

    [TestMethod]
    public void Arm64RequiresAnArmNativeInsteadOfSilentlyLoadingX64()
    {
        var platform = new RuntimePlatform("linux", "aarch64", "6.1", ':');
        var library = JsonNode.Parse("""
            {"name":"test:lwjgl:1","natives":{"linux":"natives-linux"},"downloads":{"classifiers":{"natives-linux":{},"natives-linux-arm64":{}}}}
            """)!.AsObject();
        Assert.AreEqual("natives-linux-arm64", platform.NativeClassifier(library));
        library["downloads"]!["classifiers"]!.AsObject().Remove("natives-linux-arm64");
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => platform.NativeClassifier(library));
    }

    [TestMethod]
    public void RuntimeSystemInformationDoesNotLoadWindowsLibrariesOnUnix()
    {
        Assert.IsTrue(KernelInterop.GetPhysicalMemoryBytes().Total > 0);
        Assert.IsTrue(KernelInterop.GetMemoryLoadPercent() is >= 0 and <= 100);
        Assert.IsTrue(KernelInterop.GetPerformanceLogicalProcessorCount() > 0);
        _ = SystemInfo.OSInfo;
        if (!OperatingSystem.IsWindows())
        {
            Assert.IsFalse(SystemInfo.IsGBKEncoding);
            new RegistryJavaScanner().Scan(new List<string>());
            new MicrosoftStoreJavaScanner().Scan(new List<string>());
            ProcessInterop.SetGpuPreference("/not/a/file");
        }
    }

    [TestMethod]
    public void UnixJavaParserResolvesSymlinksWithoutExecutingJava()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("This test exercises Unix-specific behavior.");
        using var temp = new TemporaryDirectory();
        var home = Path.Combine(temp.Path, "jdk");
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin/java"), "This must not be executed");
        File.WriteAllText(Path.Combine(home, "bin/javac"), "");
        File.WriteAllText(Path.Combine(home, "release"), "JAVA_VERSION=\"21.0.4\"\nOS_ARCH=\"aarch64\"\nIMPLEMENTOR=\"Eclipse Adoptium\"\n");
        var link = Path.Combine(temp.Path, "java");
        File.CreateSymbolicLink(link, Path.Combine(home, "bin/java"));
        var installation = new ReleaseFileJavaParser().Parse(link).Match(value => value, error => throw error);
        Assert.AreEqual(21, installation.MajorVersion);
        Assert.AreEqual(MachineType.ARM64, installation.Architecture);
        Assert.IsFalse(installation.IsJre);
        Assert.AreEqual(Path.Combine(home, "bin/java"), installation.JavaExePath);
        Assert.IsNull(installation.JavawExePath);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcl-test-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}
