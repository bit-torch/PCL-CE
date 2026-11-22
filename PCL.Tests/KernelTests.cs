using System.Text.Json;
using PCL.Kernel;
using PCL.Kernel.App;
using PCL.Kernel.Java;
using PCL.Kernel.Launch;
using PCL.Kernel.Loaders;
using PCL.Kernel.Mojang;
using PCL.Kernel.Versions;
using Xunit;

namespace PCL.Tests;

public sealed class RuleEvaluatorTests
{
    [Fact]
    public void NoRules_Allows()
    {
        Assert.True(RuleEvaluator.IsAllowed(null));
        Assert.True(RuleEvaluator.IsAllowed([]));
    }

    [Fact]
    public void OrderedRules_UseLastMatch()
    {
        var rules = new List<MojangRule>
        {
            new() { Action = "allow" },
            new() { Action = "disallow" }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void OsMismatch_IsRejected()
    {
        var rules = new List<MojangRule>
        {
            new()
            {
                Action = "allow",
                Os = new RuleOs { Name = "definitely-not-this-os" }
            }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void Features_MustMatch()
    {
        var rules = new List<MojangRule>
        {
            new()
            {
                Action = "allow",
                Features = new Dictionary<string, bool> { ["has_custom_resolution"] = true }
            }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules));
        Assert.True(RuleEvaluator.IsAllowed(rules,
            new Dictionary<string, bool> { ["has_custom_resolution"] = true }));
    }
}

public sealed class OfflineAccountTests
{
    [Fact]
    public void MatchesJavaOfflineUuid()
    {
        Assert.Equal("b50ad385829d3141a2167e7d7539ba7f", OfflineAccount.CreateUuid("Notch"));
    }

    [Fact]
    public void EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => OfflineAccount.CreateUuid(" "));
    }
}

public sealed class MavenPathTests
{
    [Fact]
    public void Coordinate_BecomesRepositoryPath()
    {
        Assert.Equal(
            "com/example/demo/1.0/demo-1.0.jar",
            MavenPath.FromCoordinate("com.example:demo:1.0"));
    }

    [Fact]
    public void ClassifierAndExtension_ArePreserved()
    {
        Assert.Equal(
            "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar",
            MavenPath.FromCoordinate("org.lwjgl:lwjgl:3.3.1:natives-linux"));
        Assert.Equal(
            "com/example/demo/1.0/demo-1.0.zip",
            MavenPath.FromCoordinate("com.example:demo:1.0@zip"));
    }
}

public sealed class ArgumentParserTests
{
    [Fact]
    public void Substitute_ReplacesKnownAndClearsUnknown()
    {
        var variables = new Dictionary<string, string>
        {
            ["auth_player_name"] = "Steve",
            ["version_name"] = "1.20.1"
        };

        Assert.Equal("Steve/1.20.1/", ArgumentParser.Substitute(
            "${auth_player_name}/${version_name}/${unknown}", variables));
    }

    [Fact]
    public void LegacyArguments_RespectQuotes()
    {
        Assert.Equal(
            ["--username", "Steve", "--title", "Hello World"],
            ArgumentParser.SplitLegacyArguments(
                "--username Steve --title \"Hello World\""));
    }

    [Fact]
    public void Parse_FiltersRulesAndSubstitutes()
    {
        using var document = JsonDocument.Parse("""
            {
              "game": [
                "--username",
                "${auth_player_name}",
                {
                  "rules": [{ "action": "allow", "os": { "name": "definitely-not-this-os" } }],
                  "value": "--ignored"
                },
                "--version",
                "${version_name}"
              ]
            }
            """);

        var variables = new Dictionary<string, string>
        {
            ["auth_player_name"] = "Steve",
            ["version_name"] = "1.20.1"
        };

        Assert.Equal(
            ["--username", "Steve", "--version", "1.20.1"],
            ArgumentParser.Parse(document.RootElement, "game", variables));
    }

    [Fact]
    public void Parse_FallsBackToLegacyArguments()
    {
        var variables = new Dictionary<string, string> { ["auth_player_name"] = "Steve" };

        Assert.Equal(
            ["--username", "Steve"],
            ArgumentParser.Parse(null, "game", variables, "--username ${auth_player_name}"));
    }
}

public sealed class InstalledVersionScannerTests
{
    [Fact]
    public void Scan_OrdersReleasesFirst_AndSkipsBrokenJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-tests-" + Guid.NewGuid().ToString("N"));
        var paths = new LauncherPaths(Path.Combine(root, "data"), Path.Combine(root, "game"));

        try
        {
            WriteVersion(paths, "snapshot-a", """{ "type": "snapshot" }""");
            WriteVersion(paths, "1.20.1", """{ "type": "release", "inheritsFrom": "1.20" }""",
                withJar: true);
            WriteVersion(paths, "broken", "{ not json");

            var versions = InstalledVersionScanner.Scan(paths);

            Assert.Equal(["1.20.1", "snapshot-a"], versions.Select(v => v.Id));
            Assert.Equal("1.20", versions[0].InheritsFrom);
            Assert.NotNull(versions[0].JarPath);
            Assert.Null(versions[1].JarPath);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void WriteVersion(
        LauncherPaths paths,
        string id,
        string json,
        bool withJar = false)
    {
        var directory = paths.VersionDirectory(id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".json"), json);

        if (withJar)
            File.WriteAllBytes(Path.Combine(directory, id + ".jar"), []);
    }
}

public sealed class JavaRuntimeServiceTests
{
    private const string CatalogJson = """
        {
          "linux": {
            "java-runtime-gamma": [
              {
                "manifest": {
                  "sha1": "abc123",
                  "size": 42,
                  "url": "https://example.test/linux.json"
                },
                "version": { "name": "17.0.8" }
              }
            ],
            "jre-legacy": [
              {
                "manifest": {
                  "sha1": "def456",
                  "size": 10,
                  "url": "https://example.test/legacy.json"
                },
                "version": { "name": "8u51" }
              }
            ]
          },
          "windows-x64": {}
        }
        """;

    [Fact]
    public void PlatformKey_IsKnown()
    {
        Assert.Contains(JavaRuntimeService.GetPlatformKey(), new[]
        {
            "windows-x64", "windows-x86", "windows-arm64",
            "mac-os", "mac-os-arm64",
            "linux", "linux-i386"
        });
    }

    [Fact]
    public void SelectPackage_ReturnsManifestAndVersion()
    {
        var catalog = JsonSerializer.Deserialize<JavaRuntimeCatalog>(
            CatalogJson, JsonDefaults.Options)!;

        var package = JavaRuntimeService.SelectPackage(catalog, "java-runtime-gamma", "linux");

        Assert.NotNull(package);
        Assert.Equal("17.0.8", package!.Version);
        Assert.Equal("https://example.test/linux.json", package.ManifestUrl);
        Assert.Equal("abc123", package.ManifestSha1);
        Assert.Equal(42, package.ManifestSize);
    }

    [Fact]
    public void SelectPackage_ReturnsNullForUnknownComponentOrPlatform()
    {
        var catalog = JsonSerializer.Deserialize<JavaRuntimeCatalog>(
            CatalogJson, JsonDefaults.Options)!;

        Assert.Null(JavaRuntimeService.SelectPackage(catalog, "java-runtime-gamma", "unknown"));
        Assert.Null(JavaRuntimeService.SelectPackage(catalog, "missing", "linux"));
    }

    [Fact]
    public void FindExecutable_LocatesJavaInRuntimeDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-tests-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "java-runtime-gamma-linux");

        try
        {
            var relative = OperatingSystem.IsWindows()
                ? "bin/java.exe"
                : "jre.bundle/Contents/Home/bin/java";
            var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");

            Assert.Equal(path, JavaRuntimeInstaller.FindExecutable(directory));
            Assert.Null(JavaRuntimeInstaller.FindExecutable(Path.Combine(root, "missing")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

public sealed class LoaderMetaTests
{
    [Fact]
    public void ParseLoaderVersions_HandlesFabricShape()
    {
        const string json = """
            [
              { "loader": { "version": "0.15.11", "stable": true } },
              { "loader": { "version": "0.16.0-beta.1", "stable": false } }
            ]
            """;

        var versions = LoaderMetaService.ParseLoaderVersions(json);

        Assert.Equal(2, versions.Count);
        Assert.True(versions[0].Stable);
        Assert.False(versions[1].Stable);
    }

    [Fact]
    public void ParseLoaderVersions_InfersStabilityWhenMissing()
    {
        const string json = """
            [
              { "loader": { "version": "0.20.0" } },
              { "loader": { "version": "0.20.0-beta.9" } }
            ]
            """;

        var versions = LoaderMetaService.ParseLoaderVersions(json);

        Assert.True(versions[0].Stable);
        Assert.False(versions[1].Stable);
    }

    [Fact]
    public void Endpoints_PointToMetaServices()
    {
        Assert.Equal(
            "https://meta.fabricmc.net/v2/versions/loader/1.20.1",
            LoaderMetaEndpoints.VersionsUrl(LoaderKind.Fabric, "1.20.1"));
        Assert.Equal(
            "https://meta.quiltmc.org/v3/versions/loader/1.20.1/0.20.0/profile/json",
            LoaderMetaEndpoints.ProfileUrl(LoaderKind.Quilt, "1.20.1", "0.20.0"));
    }

    [Fact]
    public void Normalize_AddsArtifactForMavenLibraries()
    {
        const string profile = """
            {
              "id": "fabric-loader-0.15.11-1.20.1",
              "inheritsFrom": "1.20.1",
              "libraries": [
                {
                  "name": "org.ow2.asm:asm:9.6",
                  "url": "https://maven.fabricmc.net/",
                  "sha1": "aa205cf0a06dbd8e04ece91c0b37c3f5d567546a",
                  "size": 123598
                },
                {
                  "name": "net.fabricmc:intermediary:1.20.1",
                  "url": "https://maven.fabricmc.net/"
                },
                {
                  "name": "com.example:kept:1.0",
                  "downloads": {
                    "artifact": {
                      "path": "com/example/kept/1.0/kept-1.0.jar",
                      "url": "https://example.test/kept.jar"
                    }
                  }
                }
              ]
            }
            """;

        var normalized = LoaderProfileNormalizer.Normalize(
            profile, "https://fallback.test/");

        using var document = JsonDocument.Parse(normalized);
        var libraries = document.RootElement.GetProperty("libraries");

        var first = libraries[0].GetProperty("downloads").GetProperty("artifact");
        Assert.Equal("org/ow2/asm/asm/9.6/asm-9.6.jar",
            first.GetProperty("path").GetString());
        Assert.Equal("https://maven.fabricmc.net/org/ow2/asm/asm/9.6/asm-9.6.jar",
            first.GetProperty("url").GetString());
        Assert.Equal("aa205cf0a06dbd8e04ece91c0b37c3f5d567546a",
            first.GetProperty("sha1").GetString());
        Assert.Equal(123598, first.GetProperty("size").GetInt64());

        var second = libraries[1].GetProperty("downloads").GetProperty("artifact");
        Assert.False(second.TryGetProperty("sha1", out _));
        Assert.False(second.TryGetProperty("size", out _));

        Assert.Equal("https://example.test/kept.jar",
            libraries[2].GetProperty("downloads").GetProperty("artifact")
                .GetProperty("url").GetString());
    }

    [Fact]
    public void Normalize_UsesFallbackMavenWhenLibraryHasNoUrl()
    {
        const string profile =
            """{ "libraries": [ { "name": "org.quiltmc:quilt-loader:0.20.0" } ] }""";

        var normalized = LoaderProfileNormalizer.Normalize(
            profile, "https://maven.quiltmc.org/repository/release");

        using var document = JsonDocument.Parse(normalized);
        Assert.Equal(
            "https://maven.quiltmc.org/repository/release/" +
            "org/quiltmc/quilt-loader/0.20.0/quilt-loader-0.20.0.jar",
            document.RootElement.GetProperty("libraries")[0]
                .GetProperty("downloads").GetProperty("artifact")
                .GetProperty("url").GetString());
    }
}

public sealed class VersionManagerTests : IDisposable
{
    private readonly string _root;
    private readonly LauncherPaths _paths;

    public VersionManagerTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "pcl-version-tests-" + Guid.NewGuid().ToString("N"));
        _paths = new LauncherPaths(
            Path.Combine(_root, "data"), Path.Combine(_root, "game"));
        _paths.EnsureDirectories();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Verify_ReportsMissingLibraryAndAssetObject()
    {
        const string id = "1.20.1";
        var directory = _paths.VersionDirectory(id);
        Directory.CreateDirectory(directory);

        var versionJson = """
            {
              "id": "1.20.1",
              "type": "release",
              "downloads": {
                "client": {
                  "path": "1.20.1.jar",
                  "url": "https://example.test/client.jar",
                  "sha1": "",
                  "size": 1
                }
              },
              "libraries": [
                {
                  "name": "g:a:1",
                  "downloads": {
                    "artifact": {
                      "path": "g/a/1/a-1.jar",
                      "url": "https://example.test/a.jar",
                      "sha1": "",
                      "size": 1
                    }
                  }
                }
              ],
              "assetIndex": {
                "id": "5",
                "sha1": "",
                "size": 1,
                "totalSize": 2,
                "url": "https://example.test/index.json"
              }
            }
            """;

        File.WriteAllText(Path.Combine(directory, id + ".json"), versionJson);
        File.WriteAllText(_paths.VersionJarPath(id), "jar");

        File.WriteAllText(
            _paths.AssetIndexPath("5"),
            """{ "objects": { "aa11": { "hash": "aa11", "size": 1 }, "bb22": { "hash": "bb22", "size": 1 } } }""");

        var objectDirectory = Path.Combine(_paths.AssetsDirectory, "objects", "aa");
        Directory.CreateDirectory(objectDirectory);
        File.WriteAllText(Path.Combine(objectDirectory, "aa11"), "asset");

        var version = new InstalledVersion(
            id, directory, Path.Combine(directory, id + ".json"),
            _paths.VersionJarPath(id), "release", null);

        var report = await new VersionManager(_paths)
            .VerifyAsync(version, TestContext.Current.CancellationToken);

        Assert.Equal(5, report.CheckedFiles);
        Assert.Equal(2, report.MissingFiles);
        Assert.Contains("g/a/1/a-1.jar", report.Missing);
        Assert.Contains("assets/bb22", report.Missing);
    }

    [Fact]
    public async Task Verify_ReportsMissingParentVersion()
    {
        const string id = "fabric-loader-test";
        var directory = _paths.VersionDirectory(id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, id + ".json"),
            """{ "id": "fabric-loader-test", "inheritsFrom": "1.20.1" }""");

        var version = new InstalledVersion(
            id, directory, Path.Combine(directory, id + ".json"),
            null, "release", "1.20.1");

        var report = await new VersionManager(_paths)
            .VerifyAsync(version, TestContext.Current.CancellationToken);

        Assert.Contains("版本 JSON：1.20.1", report.Missing);
    }

    [Fact]
    public void Delete_RemovesOnlyVersionDirectory()
    {
        var directory = _paths.VersionDirectory("temp-version");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "temp-version.json"), "{}");

        var version = new InstalledVersion(
            "temp-version", directory,
            Path.Combine(directory, "temp-version.json"),
            null, "release", null);

        new VersionManager(_paths).Delete(version);

        Assert.False(Directory.Exists(directory));
        Assert.True(Directory.Exists(_paths.VersionsDirectory));
    }

    [Fact]
    public void Delete_RejectsDirectoryOutsideVersions()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);

        var version = new InstalledVersion(
            "outside", outside, Path.Combine(outside, "outside.json"),
            null, "release", null);

        Assert.Throws<InvalidOperationException>(
            () => new VersionManager(_paths).Delete(version));
        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public void ResolveGameDirectory_HonoursIsolation()
    {
        var manager = new VersionManager(_paths);

        Assert.Equal(
            Path.Combine(_paths.VersionDirectory("1.20.1"), "game"),
            manager.ResolveGameDirectory("1.20.1", true));
        Assert.Equal(_paths.GameDirectory, manager.ResolveGameDirectory("1.20.1", false));
    }
}

public sealed class SettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "launcher.settings.json");

        try
        {
            SettingsStore.Save(path, new LauncherSettings
            {
                GameDirectory = "/games",
                MaxMemoryMb = 6144,
                OfflineUsername = "Alex",
                OfflineAccounts = ["Alex", "Steve"],
                IsolateVersions = true,
                LicensePrecheckAccepted = true
            });

            var loaded = SettingsStore.Load(path);

            Assert.Equal("/games", loaded.GameDirectory);
            Assert.Equal(6144, loaded.MaxMemoryMb);
            Assert.Equal("Alex", loaded.OfflineUsername);
            Assert.Equal(["Alex", "Steve"], loaded.OfflineAccounts);
            Assert.True(loaded.IsolateVersions);
            Assert.True(loaded.LicensePrecheckAccepted);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Normalize_SeedsAccountsFromLegacyUsername()
    {
        var settings = new LauncherSettings { OfflineUsername = "Steve" };

        settings.Normalize();

        Assert.Contains("Steve", settings.OfflineAccounts);
    }

    [Fact]
    public void Normalize_KeepsExistingAccountList()
    {
        var settings = new LauncherSettings
        {
            OfflineUsername = "Alex",
            OfflineAccounts = ["Alex", "Steve"]
        };

        settings.Normalize();

        Assert.Equal(2, settings.OfflineAccounts.Count);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "launcher.settings.json");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "{ not json");

            var loaded = SettingsStore.Load(path);

            Assert.Equal("Player", loaded.OfflineUsername);
            Assert.Equal(4096, loaded.MaxMemoryMb);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
