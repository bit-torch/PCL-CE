using System.Text.Json;
using PCL.Kernel;
using PCL.Kernel.App;
using PCL.Kernel.Launch;
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
                LicensePrecheckAccepted = true
            });

            var loaded = SettingsStore.Load(path);

            Assert.Equal("/games", loaded.GameDirectory);
            Assert.Equal(6144, loaded.MaxMemoryMb);
            Assert.Equal("Alex", loaded.OfflineUsername);
            Assert.True(loaded.LicensePrecheckAccepted);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
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
