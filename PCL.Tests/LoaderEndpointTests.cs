using PCL.Kernel.Loaders;
using Xunit;

namespace PCL.Tests;

public sealed class LoaderEndpointTests
{
    [Theory]
    [InlineData(LoaderKind.Fabric, "https://meta.fabricmc.net/v2/versions/loader/1.20.1")]
    [InlineData(LoaderKind.Quilt, "https://meta.quiltmc.org/v3/versions/loader/1.20.1")]
    [InlineData(LoaderKind.LegacyFabric, "https://meta.legacyfabric.net/v2/versions/loader/1.12.2")]
    public void VersionsUrl_UsesTheExpectedMetaHost(LoaderKind kind, string expected)
    {
        var gameVersion = kind == LoaderKind.LegacyFabric ? "1.12.2" : "1.20.1";
        Assert.Equal(expected, LoaderMetaEndpoints.VersionsUrl(kind, gameVersion));
    }

    [Theory]
    [InlineData(LoaderKind.Fabric, "https://maven.fabricmc.net/")]
    [InlineData(LoaderKind.Quilt, "https://maven.quiltmc.org/repository/release/")]
    [InlineData(LoaderKind.LegacyFabric, "https://maven.legacyfabric.net/")]
    public void MavenBase_UsesTheExpectedRepository(LoaderKind kind, string expected)
    {
        Assert.Equal(expected, LoaderMetaEndpoints.MavenBase(kind));
    }

    [Fact]
    public void MavenBase_RejectsInstallerOnlyLoaders()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LoaderMetaEndpoints.MavenBase(LoaderKind.Forge));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LoaderMetaEndpoints.MavenBase(LoaderKind.OptiFine));
    }

    [Fact]
    public void LoaderVersion_UsesDisplayWhenProvided()
    {
        Assert.Equal("HD_U_I6",
            new LoaderVersion("HD_U/I6", true, "HD_U_I6").DisplayName);
        Assert.Equal("1.20.1", new LoaderVersion("1.20.1", true).DisplayName);
    }
}
