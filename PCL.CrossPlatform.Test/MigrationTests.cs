using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using fNbt;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Avalonia;
using PCL.CrossPlatform;

namespace PCL.CrossPlatform.Test;

[TestClass]
public class MigrationTests
{
    [TestMethod]
    public async Task ArchiveRejectsSymlinksAndTraversalEvenInExcludedEntries()
    {
        using var temp = new TemporaryDirectory();
        var archive = Path.Combine(temp.Path, "unsafe.zip"); var destination = Path.Combine(temp.Path, "output"); Directory.CreateDirectory(destination);
        WriteZip(archive, ("../outside.txt", "bad"));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => InstanceFiles.ExtractAsync(archive, destination, "overrides/"));
        Assert.IsFalse(File.Exists(Path.Combine(temp.Path, "outside.txt")));
    }

    [TestMethod]
    public async Task ImportToggleArchiveAndExportPreserveDataAndExcludeCredentials()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "example.jar"); await File.WriteAllTextAsync(source, "mod");
        var root = Path.Combine(temp.Path, "instance"); var mods = Path.Combine(root, "mods");
        await InstanceFiles.ImportFileAsync(source, mods);
        await Assert.ThrowsExactlyAsync<IOException>(() => InstanceFiles.ImportFileAsync(source, mods));
        var disabled = InstanceFiles.Toggle(Path.Combine(mods, "example.jar")); Assert.IsFalse(InstanceFiles.List(mods).Single().Enabled);
        var enabled = InstanceFiles.Toggle(disabled); Assert.IsTrue(File.Exists(enabled));
        await File.WriteAllTextAsync(Path.Combine(root, "launcher_accounts.json"), "private token");
        Directory.CreateDirectory(Path.Combine(root, "PCL")); await File.WriteAllTextAsync(Path.Combine(root, "PCL/UserKey.bin"), "key");
        var archive = Path.Combine(temp.Path, "export.zip"); await InstanceFiles.ExportAsync(root, archive);
        using (var zip = ZipFile.OpenRead(archive)) CollectionAssert.AreEqual(new[] { "mods/example.jar" }, zip.Entries.Select(e => e.FullName).ToArray());
        var backup = InstanceFiles.Archive(enabled, Path.Combine(temp.Path, "backups")); Assert.AreEqual("mod", await File.ReadAllTextAsync(backup));
    }

    [TestMethod]
    public void ServerEditsPreserveUnrecognizedNbtAndCreateBackup()
    {
        using var temp = new TemporaryDirectory(); var file = Path.Combine(temp.Path, "servers.dat");
        var original = new NbtFile(new NbtCompound("")); original.RootTag.Add(new NbtString("custom", "keep"));
        var servers = new NbtList("servers", NbtTagType.Compound); servers.Add(new NbtCompound { new NbtString("name", "Old"), new NbtString("ip", "example.test"), new NbtString("icon", "keep-icon") });
        original.RootTag.Add(servers); original.SaveToFile(file, NbtCompression.None);
        ServerStore.Write(file, [new("New name", "example.test"), new("Local", "[::1]:25565")]);
        Assert.AreEqual("New name", ServerStore.Read(file)[0].Name);
        var result = new NbtFile(file); Assert.AreEqual("keep", result.RootTag.Get<NbtString>("custom")!.Value);
        Assert.AreEqual("keep-icon", ((NbtCompound)result.RootTag.Get<NbtList>("servers")![0]).Get<NbtString>("icon")!.Value);
        Assert.IsTrue(File.Exists(file + ".bak"));
    }

    [TestMethod]
    public void LegacyLoaderArgumentsRetainTheBaseGameAndIsolatedDirectory()
    {
        using var temp = new TemporaryDirectory();
        GameTests.WriteManifest(temp.Path, "base", """{"mainClass":"Main","libraries":[],"minecraftArguments":"--username ${auth_player_name} --gameDir ${game_directory}"}""");
        GameTests.WriteManifest(temp.Path, "loader", """{"inheritsFrom":"base","mainClass":"Loader","arguments":{"jvm":["-Dloader=true"],"game":["--tweakClass","ExampleTweaker"]}}""");
        File.WriteAllText(GameManifest.VersionPath(temp.Path, "base", ".jar"), "jar");
        var isolated = Path.Combine(temp.Path, "isolated data");
        var info = new GameProcess().CreateStartInfo(temp.Path, "loader", "java", LaunchAccount.Offline("Player"), 1024, "natives", new LaunchOptions
        { CaptureLogs = false, GameDirectory = isolated, Environment = new Dictionary<string, string> { ["PCL_TEST"] = "yes" }, JvmArguments = ["-Dcustom=some value"] });
        Assert.IsFalse(info.RedirectStandardOutput); Assert.IsFalse(info.RedirectStandardError);
        var args = info.ArgumentList.ToArray();
        Assert.Contains("--username", args); Assert.Contains("--tweakClass", args); Assert.Contains("-cp", args);
        Assert.Contains("-Dcustom=some value", args); Assert.Contains(isolated, args);
        Assert.AreEqual(isolated, info.WorkingDirectory); Assert.AreEqual("yes", info.Environment["PCL_TEST"]);
    }

    [TestMethod]
    public void QuickPlayUsesModernFlagsAndAvoidsObsoleteServerArguments()
    {
        using var temp = new TemporaryDirectory();
        GameTests.WriteManifest(temp.Path, "test", """
            {"mainClass":"Main","libraries":[],"arguments":{"game":[
              {"rules":[{"action":"allow","features":{"is_quick_play_multiplayer":true}}],"value":["--quickPlayMultiplayer","${quickPlayMultiplayer}"]}]}}
            """);
        File.WriteAllText(GameManifest.VersionPath(temp.Path, "test", ".jar"), "jar");
        var info = new GameProcess().CreateStartInfo(temp.Path, "test", "java", LaunchAccount.Offline("Player"), 1024, "natives", new LaunchOptions { ServerAddress = "server.test:25566" });
        var args = info.ArgumentList.ToArray(); Assert.Contains("--quickPlayMultiplayer", args); Assert.Contains("server.test:25566", args); Assert.DoesNotContain("--server", args);
    }

    [TestMethod]
    public void DownloadMirrorNeverRoutesAuthenticationOrArbitraryHosts()
    {
        const string mirror = "https://mirror.test";
        Assert.AreEqual("https://mirror.test/assets/a/b", DownloadRoutingHandler.Map(new Uri("https://resources.download.minecraft.net/a/b"), mirror).AbsoluteUri);
        Assert.AreEqual("https://mirror.test/maven/a.jar", DownloadRoutingHandler.Map(new Uri("https://libraries.minecraft.net/a.jar"), mirror).AbsoluteUri);
        Assert.AreEqual("https://api.minecraftservices.com/minecraft/profile", DownloadRoutingHandler.Map(new Uri("https://api.minecraftservices.com/minecraft/profile"), mirror).AbsoluteUri);
        Assert.ThrowsExactly<ArgumentException>(() => DownloadRoutingHandler.Map(new Uri("https://piston-meta.mojang.com/x"), "http://mirror.test"));
    }

    [TestMethod]
    public void ExistingCrashRulesRemainAvailableToPortableHost()
    {
        var results = PortableDiagnostics.Analyze("java.lang.OutOfMemoryError: Java heap space\n at net.minecraft.client.Main.main(Main.java:1)");
        Assert.IsTrue(results.Any(f => f.Cause == "OutOfMemory"));
    }

    [TestMethod]
    public void CustomHomeNeverEvaluatesCodeOrMarkupExtensions()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => CustomHomeRenderer.Render("<TextBlock Text=\"{Binding Dangerous}\" />", _ => { }));
        Assert.ThrowsExactly<InvalidDataException>(() => CustomHomeRenderer.Render("<Button Content=\"Run\" EventData=\"file:///tmp/secret\" />", _ => { }));
        Assert.ThrowsExactly<System.Xml.XmlException>(() => CustomHomeRenderer.Render("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///etc/passwd'>]><TextBlock>&a;</TextBlock>", _ => { }));
    }

    internal static void WriteZip(string path, params (string Name, string Content)[] entries)
    {
        using var output = new FileStream(path, FileMode.Create); using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var (name, content) in entries) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(content); }
    }
}

internal sealed class FixtureHttp : HttpMessageHandler
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public List<string> Requests { get; } = [];
    public List<string?> ApiKeys { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var url = request.RequestUri!.AbsoluteUri;
        lock (Requests) { Requests.Add(url); ApiKeys.Add(request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null); }
        var match = Files.Keys.OrderByDescending(key => key.Length).FirstOrDefault(key => url == key || key.EndsWith('*') && url.StartsWith(key[..^1], StringComparison.Ordinal));
        return Task.FromResult(new HttpResponseMessage(match is null ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new ByteArrayContent(match is null ? [] : Files[match]) });
    }
    public void Json(string url, JsonNode json) => Files[url] = Encoding.UTF8.GetBytes(json.ToJsonString());
    public void Json(string url, string json) => Files[url] = Encoding.UTF8.GetBytes(json);
    public static string Hash(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
}
