using PCL.Kernel.Loaders;
using Xunit;

namespace PCL.Tests;

public sealed class ForgeLoaderTests
{
    private const string ForgeMetadata = """
        <metadata>
          <versioning>
            <versions>
              <version>1.20.1-47.1.0</version>
              <version>1.20.1-47.2.0</version>
              <version>1.20.2-48.0.0</version>
              <version>1.7.10-10.13.4.1614</version>
            </versions>
          </versioning>
        </metadata>
        """;

    private const string NeoForgeMetadata = """
        <metadata>
          <versioning>
            <versions>
              <version>20.1.1</version>
              <version>20.6.119</version>
              <version>21.0.167</version>
              <version>21.1.79</version>
              <version>21.1.80</version>
            </versions>
          </versioning>
        </metadata>
        """;

    [Fact]
    public void ParseMavenVersions_FiltersForgeByGameVersion_AndSortsDescending()
    {
        var versions = ForgeLoaderInstaller.ParseMavenVersions(
            LoaderKind.Forge, "1.20.1", ForgeMetadata);

        Assert.Equal(["47.2.0", "47.1.0"], versions.Select(v => v.Version));
        Assert.All(versions, version => Assert.True(version.Stable));
    }

    [Fact]
    public void ParseMavenVersions_MapsNeoForgeToMinecraftVersion()
    {
        var patch = ForgeLoaderInstaller.ParseMavenVersions(
            LoaderKind.NeoForge, "1.21.1", NeoForgeMetadata);
        var minor = ForgeLoaderInstaller.ParseMavenVersions(
            LoaderKind.NeoForge, "1.21", NeoForgeMetadata);
        var legacy = ForgeLoaderInstaller.ParseMavenVersions(
            LoaderKind.NeoForge, "1.20.1", NeoForgeMetadata);

        Assert.Equal(["21.1.80", "21.1.79"], patch.Select(v => v.Version));
        Assert.Equal(["21.0.167"], minor.Select(v => v.Version));
        Assert.Empty(legacy);
    }

    [Theory]
    [InlineData("21.1.80", "1.21.1")]
    [InlineData("21.0.167", "1.21")]
    [InlineData("20.6.119", "1.20.6")]
    [InlineData("20.4.237", "1.20.4")]
    [InlineData("20.2.86", "1.20.2")]
    [InlineData("20.1.1", null)]
    [InlineData("not-a-version", null)]
    public void NeoForgeMinecraftVersion_HandlesKnownShapes(string input, string? expected)
    {
        Assert.Equal(expected, ForgeLoaderInstaller.NeoForgeMinecraftVersion(input));
    }

    [Fact]
    public void Endpoints_BuildInstallerUrlsAndVersionIds()
    {
        Assert.Equal(
            "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar",
            LoaderMetaEndpoints.ForgeInstallerUrl("1.20.1", "47.2.0"));
        Assert.Equal(
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.80/neoforge-21.1.80-installer.jar",
            LoaderMetaEndpoints.NeoForgeInstallerUrl("21.1.80"));
        Assert.Equal("1.20.1-forge-47.2.0",
            LoaderMetaEndpoints.ForgeVersionId("1.20.1", "47.2.0"));
        Assert.Equal("neoforge-21.1.80",
            LoaderMetaEndpoints.NeoForgeVersionId("21.1.80"));
    }

    [Fact]
    public void ParseMavenVersions_InvalidXml_ReturnsEmpty()
    {
        Assert.Empty(ForgeLoaderInstaller.ParseMavenVersions(
            LoaderKind.Forge, "1.20.1", "not xml"));
    }
}
