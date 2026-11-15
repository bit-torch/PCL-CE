using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.CrossPlatform;

namespace PCL.CrossPlatform.Test;

[TestClass]
public class GameTests
{
    [TestMethod]
    public void DownloadAndArchivePathsCannotEscapeTheirRoot()
    {
        using var temp = new TemporaryDirectory();
        foreach (var path in new[] { "../escape", "/escape", "C:/escape", "..\\escape", "a/../../escape" })
            Assert.ThrowsExactly<InvalidDataException>(() => GameManifest.SafePath(temp.Path, path));
        Assert.AreEqual(Path.Combine(temp.Path, "a", "b.jar"), GameManifest.SafePath(temp.Path, "a/b.jar"));
        Assert.ThrowsExactly<InvalidDataException>(() => GameManifest.ValidateId("../version"));
    }

    [TestMethod]
    public void InheritedVersionsKeepBaseJarAndReplaceLibraryVersions()
    {
        using var temp = new TemporaryDirectory();
        WriteManifest(temp.Path, "base", """
            {"id":"base","mainClass":"BaseMain","libraries":[{"name":"a:b:1"}],"arguments":{"jvm":["-Dbase=true"],"game":["--username","${auth_player_name}"]}}
            """);
        WriteManifest(temp.Path, "modded", """
            {"id":"modded","inheritsFrom":"base","mainClass":"LoaderMain","libraries":[{"name":"a:b:2"}],"arguments":{"jvm":["-Dloader=true"]}}
            """);
        var manifest = GameManifest.Load(temp.Path, "modded");
        Assert.AreEqual("base", manifest["jar"]!.ToString());
        Assert.AreEqual("LoaderMain", manifest["mainClass"]!.ToString());
        Assert.AreEqual(1, manifest["libraries"]!.AsArray().Count);
        Assert.AreEqual("a:b:2", manifest["libraries"]![0]!["name"]!.ToString());
        Assert.AreEqual(2, manifest["arguments"]!["jvm"]!.AsArray().Count);
        WriteManifest(temp.Path, "loop", """{"inheritsFrom":"loop"}""");
        Assert.ThrowsExactly<InvalidDataException>(() => GameManifest.Load(temp.Path, "loop"));
    }

    [TestMethod]
    public void LaunchUsesArgumentListAndHostSpecificClasspathAndNatives()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game with spaces");
        WriteManifest(root, "base", """
            {"id":"base","mainClass":"Main","libraries":[
              {"name":"a:b:1","downloads":{"artifact":{"path":"a/b.jar"}}},
              {"name":"win:only:1","rules":[{"action":"allow","os":{"name":"windows"}}],"downloads":{"artifact":{"path":"win.jar"}}}
            ],"arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${classpath}"],
            "game":["--username","${auth_player_name}","--gameDir","${game_directory}",
              {"rules":[{"action":"allow","features":{"is_demo_user":true}}],"value":"--demo"}]}}
            """);
        Directory.CreateDirectory(Path.Combine(root, "libraries/a"));
        File.WriteAllText(Path.Combine(root, "libraries/a/b.jar"), "jar");
        File.WriteAllText(GameManifest.VersionPath(root, "base", ".jar"), "jar");
        foreach (var os in new[] { "linux", "osx" })
        {
            var info = new GameProcess(new RuntimePlatform(os, "amd64", "1", ':'))
                .CreateStartInfo(root, "base", "/java", LaunchAccount.Offline("Player"), 2048, "/native dir");
            var args = info.ArgumentList.ToList();
            Assert.AreEqual("-Djava.library.path=/native dir", args[1]);
            var classpath = args[args.IndexOf("-cp") + 1];
            Assert.IsTrue(classpath.Contains(':'));
            Assert.IsFalse(classpath.Contains("win.jar"));
            Assert.IsFalse(args.Contains("--demo"));
            Assert.IsTrue(args.Contains(Path.GetFullPath(root)));
            Assert.AreEqual(os == "osx", args.Contains("-XstartOnFirstThread"));
            Assert.AreEqual("", info.Arguments);
            Assert.IsFalse(info.UseShellExecute);
        }
        Assert.AreEqual("a01e3843e5213998958af459800e4d11", LaunchAccount.Offline("Player").Uuid);
    }

    [TestMethod]
    public async Task DownloadsValidateHashesAndPreserveExistingFilesOnFailure()
    {
        using var temp = new TemporaryDirectory();
        var data = Encoding.UTF8.GetBytes("valid file");
        var hash = Convert.ToHexString(SHA1.HashData(data));
        var handler = new FakeHandler(data);
        using var client = new HttpClient(handler);
        var installer = new GameInstaller(client);
        var target = Path.Combine(temp.Path, "file.jar");
        await installer.DownloadAsync("https://example.test/file", target, hash, data.Length, default);
        await installer.DownloadAsync("https://example.test/file", target, hash, data.Length, default);
        Assert.AreEqual(1, handler.Requests);
        File.WriteAllText(target, "keep me");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => installer.DownloadAsync(
            "https://example.test/file", target, new string('0', 40), data.Length, default));
        Assert.AreEqual("keep me", File.ReadAllText(target));
        Assert.AreEqual(0, Directory.GetFiles(temp.Path, "*.part").Length);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => installer.DownloadAsync(
            "http://example.test/file", target, hash, data.Length, default));
    }

    [TestMethod]
    public async Task CancellationLeavesNoPartialFile()
    {
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new FakeHandler([1, 2]));
        var target = Path.Combine(temp.Path, "file.jar");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new GameInstaller(client)
            .DownloadAsync("https://example.test/file", target, null, null, cancellation.Token));
        Assert.IsFalse(File.Exists(target));
        Assert.AreEqual(0, Directory.GetFiles(temp.Path, "*.part").Length);
    }

    internal static void WriteManifest(string root, string id, string content)
    {
        var path = GameManifest.VersionPath(root, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
    }

    private sealed class FakeHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
