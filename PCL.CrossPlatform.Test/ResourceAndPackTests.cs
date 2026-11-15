using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.CrossPlatform;

namespace PCL.CrossPlatform.Test;

[TestClass]
public class ResourceAndPackTests
{
    [TestMethod]
    public async Task RequiredResourceDependenciesAreResolvedOnceWithCyclesAndHashes()
    {
        using var temp = new TemporaryDirectory(); var handler = new FixtureHttp(); using var client = new HttpClient(handler);
        var a = Release("a", "b"); var b = Release("b", "a");
        handler.Json("https://api.modrinth.com/v2/version/a", a); handler.Json("https://api.modrinth.com/v2/version/b", b);
        handler.Files["https://files.test/a.jar"] = Encoding.UTF8.GetBytes("a"); handler.Files["https://files.test/b.jar"] = Encoding.UTF8.GetBytes("b");
        var files = await new ResourceCatalog(client).InstallAsync(ResourceProvider.Modrinth, new("a", "A", a), temp.Path, "1.20.1", "fabric", default);
        Assert.AreEqual(2, files.Count); Assert.AreEqual("a", File.ReadAllText(Path.Combine(temp.Path, "a.jar"))); Assert.AreEqual("b", File.ReadAllText(Path.Combine(temp.Path, "b.jar")));
        Assert.AreEqual(1, handler.Requests.Count(url => url == "https://files.test/a.jar"));
    }

    [TestMethod]
    public async Task CurseForgeRespectsAuthorDownloadRestrictions()
    {
        using var temp = new TemporaryDirectory(); using var client = new HttpClient(new FixtureHttp());
        var release = JsonNode.Parse("""{"id":1,"fileName":"restricted.jar","downloadUrl":null,"dependencies":[]}""")!.AsObject();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new ResourceCatalog(client, "test-key").InstallAsync(ResourceProvider.CurseForge, new("1", "restricted", release), temp.Path, "", "", default));
        Assert.AreEqual(0, Directory.GetFiles(temp.Path).Length);
    }

    [TestMethod]
    public async Task FabricInstallRetainsParentVersionAndDownloadsItsLibraries()
    {
        using var temp = new TemporaryDirectory(); var handler = BaseGame(); using var client = new HttpClient(handler);
        handler.Json("https://meta.fabricmc.net/profile", """{"id":"fabric-test","inheritsFrom":"base","mainClass":"KnotClient","libraries":[{"name":"fabric:loader:1","downloads":{"artifact":{"path":"fabric/loader.jar","url":"https://files.test/loader.jar"}}}]}""");
        handler.Files["https://files.test/loader.jar"] = [1, 2, 3];
        var id = await new LoaderInstaller(client).InstallAsync(temp.Path, "java", new(LoaderKind.Fabric, "1", "base", "https://meta.fabricmc.net/profile"), null, default);
        Assert.AreEqual("fabric-test", id); Assert.IsTrue(File.Exists(GameManifest.VersionPath(temp.Path, "base", ".jar")));
        Assert.AreEqual("base", GameManifest.Load(temp.Path, id)["jar"]!.ToString()); Assert.IsTrue(File.Exists(Path.Combine(temp.Path, "libraries/fabric/loader.jar")));
    }

    [TestMethod]
    public async Task FailedLoaderInstallRemovesNewProfileButPreservesBase()
    {
        using var temp = new TemporaryDirectory(); var handler = BaseGame(); using var client = new HttpClient(handler);
        handler.Json("https://meta.fabricmc.net/profile", """{"id":"broken-loader","inheritsFrom":"base","libraries":[{"name":"missing:library:1","downloads":{"artifact":{"path":"missing.jar","url":"https://files.test/missing.jar"}}}]}""");
        await Assert.ThrowsAsync<HttpRequestException>(() => new LoaderInstaller(client).InstallAsync(temp.Path, "java", new(LoaderKind.Fabric, "1", "base", "https://meta.fabricmc.net/profile"), null, default));
        Assert.IsFalse(File.Exists(GameManifest.VersionPath(temp.Path, "broken-loader"))); Assert.IsTrue(File.Exists(GameManifest.VersionPath(temp.Path, "base")));
    }

    [TestMethod]
    public async Task ModrinthPackSkipsServerOnlyAndOptionalFilesAndPublishesAtomically()
    {
        using var temp = new TemporaryDirectory(); var handler = BaseGame(); using var client = new HttpClient(handler);
        var pack = Path.Combine(temp.Path, "pack.mrpack"); var data = Encoding.UTF8.GetBytes("mod"); handler.Files["https://files.test/mod.jar"] = data;
        var file = new JsonObject { ["path"] = "mods/mod.jar", ["fileSize"] = data.Length, ["hashes"] = new JsonObject { ["sha1"] = FixtureHttp.Hash(data) }, ["downloads"] = new JsonArray("https://files.test/mod.jar") };
        var optional = file.DeepClone(); optional!["path"] = "mods/optional.jar"; optional["env"] = new JsonObject { ["client"] = "optional" };
        var server = file.DeepClone(); server!["path"] = "mods/server.jar"; server["env"] = new JsonObject { ["client"] = "unsupported" };
        var index = new JsonObject { ["formatVersion"] = 1, ["game"] = "minecraft", ["dependencies"] = new JsonObject { ["minecraft"] = "base" }, ["files"] = new JsonArray(file, optional, server) };
        MigrationTests.WriteZip(pack, ("modrinth.index.json", index.ToJsonString()), ("overrides/config/example.txt", "settings"));
        var destination = Path.Combine(temp.Path, "installed"); var result = await new ModpackInstaller(client).InstallAsync(pack, destination, "java", false, null, default);
        Assert.AreEqual("base", result.VersionId); Assert.AreEqual("mod", File.ReadAllText(Path.Combine(destination, "mods/mod.jar")));
        Assert.IsFalse(File.Exists(Path.Combine(destination, "mods/server.jar"))); Assert.IsFalse(File.Exists(Path.Combine(destination, "mods/optional.jar")));
        Assert.AreEqual("settings", File.ReadAllText(Path.Combine(destination, "config/example.txt")));
        Assert.AreEqual(0, Directory.GetDirectories(temp.Path, "*.install-*").Length);
    }

    [TestMethod]
    public async Task FailedPackLeavesNoPublishedInstanceOrStagingDirectory()
    {
        using var temp = new TemporaryDirectory(); var handler = BaseGame(); using var client = new HttpClient(handler);
        var pack = Path.Combine(temp.Path, "pack.mrpack");
        MigrationTests.WriteZip(pack, ("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"base"},"files":[]}"""), ("../escape", "bad"));
        var destination = Path.Combine(temp.Path, "installed");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new ModpackInstaller(client).InstallAsync(pack, destination, "java", false, null, default));
        Assert.IsFalse(Directory.Exists(destination)); Assert.AreEqual(0, Directory.GetDirectories(temp.Path, "*.install-*").Length);
    }

    [TestMethod]
    public async Task MultiMcPackInstallsComponentsAndAppliesLocalPatches()
    {
        using var temp = new TemporaryDirectory(); var handler = BaseGame(); using var client = new HttpClient(handler);
        var pack = Path.Combine(temp.Path, "pack.zip");
        MigrationTests.WriteZip(pack, ("MyPack/mmc-pack.json", """{"components":[{"uid":"net.minecraft","version":"base"}]}"""),
            ("MyPack/.minecraft/options.txt", "music:0"), ("MyPack/patches/custom.json", """{"uid":"custom","mainClass":"CustomMain","+jvmArgs":["-Dcustom=true"]}"""));
        var destination = Path.Combine(temp.Path, "installed"); var result = await new ModpackInstaller(client).InstallAsync(pack, destination, "java", false, null, default);
        Assert.AreEqual("pack-custom", result.VersionId); Assert.AreEqual("CustomMain", GameManifest.Load(destination, result.VersionId)["mainClass"]!.ToString());
        Assert.AreEqual("music:0", File.ReadAllText(Path.Combine(destination, "options.txt")));
    }

    private static JsonObject Release(string id, string dependency) => new()
    {
        ["id"] = id, ["name"] = id,
        ["dependencies"] = new JsonArray(new JsonObject { ["dependency_type"] = "required", ["version_id"] = dependency }),
        ["files"] = new JsonArray(new JsonObject { ["filename"] = id + ".jar", ["url"] = "https://files.test/" + id + ".jar", ["hashes"] = new JsonObject { ["sha1"] = FixtureHttp.Hash(Encoding.UTF8.GetBytes(id)) } })
    };
    internal static FixtureHttp BaseGame()
    {
        var handler = new FixtureHttp(); var data = Encoding.UTF8.GetBytes("client jar"); handler.Files["https://files.test/client.jar"] = data;
        var game = new JsonObject { ["id"] = "base", ["mainClass"] = "Main", ["libraries"] = new JsonArray(), ["downloads"] = new JsonObject { ["client"] = new JsonObject { ["url"] = "https://files.test/client.jar", ["sha1"] = FixtureHttp.Hash(data), ["size"] = data.Length } } };
        handler.Json("https://files.test/base.json", game);
        handler.Json(GameInstaller.VersionManifestUrl, new JsonObject { ["versions"] = new JsonArray(new JsonObject { ["id"] = "base", ["type"] = "release", ["url"] = "https://files.test/base.json", ["sha1"] = FixtureHttp.Hash(Encoding.UTF8.GetBytes(game.ToJsonString())) }) });
        return handler;
    }
}
