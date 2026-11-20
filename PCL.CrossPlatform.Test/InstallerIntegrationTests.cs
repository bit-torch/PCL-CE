using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.CrossPlatform;

namespace PCL.CrossPlatform.Test;

[TestClass]
public class InstallerIntegrationTests
{
    [TestMethod]
    public async Task InstallSelectsLinuxFilesDownloadsAssetsAndRepairsCorruption()
    {
        using var temp = new TemporaryDirectory();
        var artifact = Encoding.UTF8.GetBytes("library");
        var native = Zip("native.so", "native library");
        var jar = Encoding.UTF8.GetBytes("client jar");
        var asset = Encoding.UTF8.GetBytes("game asset");
        var hash = Convert.ToHexString(SHA1.HashData(asset)).ToLowerInvariant();
        var index = Encoding.UTF8.GetBytes(new JsonObject { ["objects"] = new JsonObject
        { ["a/test.txt"] = new JsonObject { ["hash"] = hash, ["size"] = asset.Length } } }.ToJsonString());
        var manifest = new JsonObject
        {
            ["id"] = "test", ["mainClass"] = "Main",
            ["downloads"] = new JsonObject { ["client"] = Download("client", jar) },
            ["assetIndex"] = Download("index", index),
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "test:library:1", ["natives"] = new JsonObject { ["linux"] = "natives-linux" },
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = Download("library", artifact, "test/library.jar"),
                    ["classifiers"] = new JsonObject { ["natives-linux"] = Download("native", native, "test/native.jar") }
                }
            }, new JsonObject
            {
                ["name"] = "windows:only:1", ["rules"] = JsonNode.Parse("""[{"action":"allow","os":{"name":"windows"}}]"""),
                ["downloads"] = new JsonObject { ["artifact"] = Download("not-requested", artifact, "windows.jar") }
            })
        };
        manifest["assetIndex"]!["id"] = "test-assets";
        var metadata = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var responses = new Dictionary<string, byte[]>
        {
            ["https://example.test/metadata"] = metadata, ["https://example.test/client"] = jar,
            ["https://example.test/library"] = artifact, ["https://example.test/native"] = native,
            ["https://example.test/index"] = index,
            [$"https://resources.download.minecraft.net/{hash[..2]}/{hash}"] = asset
        };
        var handler = new MappedHandler(responses);
        using var client = new HttpClient(handler);
        var installer = new GameInstaller(client, new RuntimePlatform("linux", "amd64", "6.1", ':'));
        var version = new AvailableVersion("test", "release", "https://example.test/metadata", Convert.ToHexString(SHA1.HashData(metadata)));
        await installer.InstallAsync(temp.Path, version);
        Assert.AreEqual(6, handler.Requests);
        Assert.IsFalse(File.Exists(Path.Combine(temp.Path, "libraries/windows.jar")));
        Assert.IsTrue(File.Exists(Path.Combine(temp.Path, "assets/objects", hash[..2], hash)));
        File.WriteAllText(Path.Combine(temp.Path, "libraries/test/library.jar"), "bad data");
        await installer.RepairAsync(temp.Path, "test");
        Assert.AreEqual(7, handler.Requests);
        CollectionAssert.AreEqual(artifact, File.ReadAllBytes(Path.Combine(temp.Path, "libraries/test/library.jar")));
    }

    [TestMethod]
    public void NativeArchiveTraversalIsRejectedBeforeStartingJava()
    {
        using var temp = new TemporaryDirectory();
        GameTests.WriteManifest(temp.Path, "test", """
            {"mainClass":"Main","libraries":[{"name":"x:y:1","natives":{"linux":"natives-linux"},
            "downloads":{"classifiers":{"natives-linux":{"path":"bad.jar"}}}}]}
            """);
        Directory.CreateDirectory(Path.Combine(temp.Path, "libraries"));
        File.WriteAllBytes(Path.Combine(temp.Path, "libraries/bad.jar"), Zip("../escape.so", "bad"));
        Assert.ThrowsExactly<InvalidDataException>(() => new GameProcess(new RuntimePlatform("linux", "amd64", "6.1", ':'))
            .Start(temp.Path, "test", "must-never-start", LaunchAccount.Offline("Player"), 1024));
    }

    [TestMethod]
    public async Task UnixLaunchStartsProcessWithSpacesAndCleansNativeDirectory()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("This test exercises Unix-specific behavior.");
        using var temp = new TemporaryDirectory();
        GameTests.WriteManifest(temp.Path, "test", """{"mainClass":"Main","libraries":[],"minecraftArguments":"--username ${auth_player_name}"}""");
        File.WriteAllText(GameManifest.VersionPath(temp.Path, "test", ".jar"), "test");
        var java = Path.Combine(temp.Path, "fake java");
        await File.WriteAllTextAsync(java, "#!/bin/sh\nprintf '%s\\n' \"$@\"\n");
        File.SetUnixFileMode(java, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var lines = new List<string>();
        using var process = new GameProcess().Start(temp.Path, "test", java, LaunchAccount.Offline("Player"), 1024,
            line => { lock (lines) lines.Add(line); });
        await process.WaitForExitAsync();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode);
        lock (lines)
        {
            Assert.Contains("Player", lines);
            Assert.Contains("Main", lines);
            var nativeArg = lines.Single(line => line.StartsWith("-Djava.library.path="));
            Assert.IsFalse(Directory.Exists(nativeArg["-Djava.library.path=".Length..]));
        }
    }

    private static JsonObject Download(string file, byte[] bytes, string? path = null)
    {
        var result = new JsonObject
        {
            ["url"] = "https://example.test/" + file,
            ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)), ["size"] = bytes.Length
        };
        if (path is not null) result["path"] = path;
        return result;
    }
    private static byte[] Zip(string name, string content)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(content);
        return output.ToArray();
    }
    private sealed class MappedHandler(Dictionary<string, byte[]> responses) : HttpMessageHandler
    {
        private int _requests;
        public int Requests => _requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            var data = responses[request.RequestUri!.ToString()];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
    }
}
