using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PCL.Kernel.GameData;
using PCL.Kernel.Modpacks;
using PCL.Kernel.Net;
using PCL.Kernel.Updates;
using Xunit;

namespace PCL.Tests;

public sealed class ToolsAndModpackTests
{
    [Fact]
    public async Task MrPack_InstallsFilesAndOverrides()
    {
        var root = CreateTempDirectory();
        try
        {
            var archive = Path.Combine(root, "pack.mrpack");
            var gameDirectory = Path.Combine(root, "game");
            Directory.CreateDirectory(gameDirectory);

            var modBytes = Encoding.UTF8.GetBytes("example-mod-content");
            var sha1 = Convert.ToHexString(SHA1.HashData(modBytes)).ToLowerInvariant();

            var index = new JsonObject
            {
                ["formatVersion"] = 1,
                ["game"] = "minecraft",
                ["name"] = "Example Pack",
                ["versionId"] = "1.0.0",
                ["summary"] = "A test pack",
                ["dependencies"] = new JsonObject
                {
                    ["minecraft"] = "1.20.1",
                    ["fabric-loader"] = "0.15.0"
                },
                ["files"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["path"] = "mods/example.jar",
                        ["downloads"] = new JsonArray("https://cdn.example/example.jar"),
                        ["hashes"] = new JsonObject { ["sha1"] = sha1 },
                        ["fileSize"] = modBytes.Length,
                        ["env"] = new JsonObject { ["client"] = "required" }
                    },
                    new JsonObject
                    {
                        ["path"] = "mods/server-only.jar",
                        ["downloads"] = new JsonArray("https://cdn.example/server-only.jar"),
                        ["hashes"] = new JsonObject(),
                        ["fileSize"] = 4,
                        ["env"] = new JsonObject { ["client"] = "unsupported" }
                    }
                }
            };

            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "modrinth.index.json", index.ToJsonString());
                WriteEntry(zip, "overrides/config/example.toml", "enabled = true");
                WriteEntry(zip, "client-overrides/options.txt", "language:zh_cn");
            }

            using var handler = new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(modBytes)
                });
            using var downloads = new DownloadClient(new HttpClient(handler));
            var installer = new MrPackInstaller(gameDirectory, downloads);

            var result = await installer.InstallAsync(
                archive, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("Example Pack", result.Name);
            Assert.Equal("1.20.1", result.MinecraftVersion);
            Assert.Single(result.Files);
            Assert.Equal(2, result.Overrides.Count);

            Assert.True(File.Exists(Path.Combine(gameDirectory, "mods", "example.jar")));
            Assert.Equal(modBytes, await File.ReadAllBytesAsync(
                Path.Combine(gameDirectory, "mods", "example.jar"),
                TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(gameDirectory, "mods", "server-only.jar")));
            Assert.True(File.Exists(Path.Combine(gameDirectory, "config", "example.toml")));
            Assert.True(File.Exists(Path.Combine(gameDirectory, "options.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MrPack_ReadManifest_ParsesDependencies()
    {
        var root = CreateTempDirectory();
        try
        {
            var archive = Path.Combine(root, "pack.mrpack");
            var index = new JsonObject
            {
                ["formatVersion"] = 1,
                ["game"] = "minecraft",
                ["name"] = "Pack",
                ["versionId"] = "2.0",
                ["dependencies"] = new JsonObject
                {
                    ["minecraft"] = "1.19.2",
                    ["forge"] = "43.2.0"
                },
                ["files"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["path"] = "mods/a.jar",
                        ["downloads"] = new JsonArray("https://cdn.example/a.jar"),
                        ["hashes"] = new JsonObject { ["sha512"] = "deadbeef" },
                        ["fileSize"] = 10
                    }
                }
            };

            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                WriteEntry(zip, "modrinth.index.json", index.ToJsonString());

            var manifest = MrPackInstaller.ReadManifest(archive);

            Assert.Equal("Pack", manifest.Name);
            Assert.Equal("1.19.2", manifest.MinecraftVersion);
            Assert.Equal("43.2.0", manifest.Dependencies["forge"]);
            var file = Assert.Single(manifest.Files);
            Assert.Equal("mods/a.jar", file.Path);
            Assert.Equal("deadbeef", file.Sha512);
            Assert.False(file.SkipOnClient);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveScanner_ReadsWorldsWithIconAndOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var saves = Path.Combine(root, "saves");
            var older = Path.Combine(saves, "Older");
            var newer = Path.Combine(saves, "Newer");
            Directory.CreateDirectory(older);
            Directory.CreateDirectory(newer);
            File.WriteAllBytes(Path.Combine(newer, "icon.png"), [1, 2, 3]);
            Directory.SetLastWriteTimeUtc(older, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Directory.SetLastWriteTimeUtc(newer, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var result = SaveScanner.Scan(root);

            Assert.Equal(2, result.Count);
            Assert.Equal("Newer", result[0].Name);
            Assert.True(result[0].HasIcon);
            Assert.Equal("Older", result[1].Name);
            Assert.False(result[1].HasIcon);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScreenshotScanner_FiltersImageExtensions()
    {
        var root = CreateTempDirectory();
        try
        {
            var screenshots = Path.Combine(root, "screenshots");
            Directory.CreateDirectory(screenshots);
            File.WriteAllBytes(Path.Combine(screenshots, "a.png"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(screenshots, "b.jpg"), new byte[1024]);
            File.WriteAllText(Path.Combine(screenshots, "notes.txt"), "ignore");

            var result = ScreenshotScanner.Scan(root);

            Assert.Equal(2, result.Count);
            Assert.DoesNotContain(result, entry => entry.FileName == "notes.txt");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ServerList_ParsesGzipNbt()
    {
        var root = CreateTempDirectory();
        try
        {
            var data = BuildServersDat("测试服务器", "mc.example.com:25565");
            File.WriteAllBytes(Path.Combine(root, "servers.dat"), data);

            var servers = ServerList.Read(root);

            var server = Assert.Single(servers);
            Assert.Equal("测试服务器", server.Name);
            Assert.Equal("mc.example.com:25565", server.Ip);
            Assert.Equal("测试服务器", server.DisplayName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LogAnalyzer_MatchesKnownIssues()
    {
        var root = CreateTempDirectory();
        try
        {
            var logs = Path.Combine(root, "logs");
            var crashes = Path.Combine(root, "crash-reports");
            Directory.CreateDirectory(logs);
            Directory.CreateDirectory(crashes);
            File.WriteAllText(Path.Combine(logs, "latest.log"),
                "[12:00:00] [main/ERROR]: java.lang.OutOfMemoryError: Java heap space\n");
            File.WriteAllText(Path.Combine(crashes, "crash-2025-01-01.txt"),
                "java.lang.UnsupportedClassVersionError: net/minecraft/client/Main\n");

            var result = LogAnalyzer.Analyze(root);

            Assert.Equal(2, result.FilesScanned);
            Assert.Contains(result.Issues, issue => issue.Title == "内存不足");
            Assert.Contains(result.Issues, issue => issue.Title == "Java 版本不匹配");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UpdateChecker_ParsesLatestRelease()
    {
        Uri? requested = null;
        string? accept = null;

        using var handler = new StubHandler(request =>
        {
            requested = request.RequestUri;
            accept = request.Headers.Accept.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"tag_name":"v1.2.0","html_url":"https://github.com/example/repo/releases/tag/v1.2.0",
                    "body":"Release notes",
                    "assets":[{"browser_download_url":"https://github.com/example/repo/app.zip"}]}
                    """, Encoding.UTF8, "application/json")
            };
        });

        using var checker = new UpdateChecker(new HttpClient(handler), "example", "repo");
        var info = await checker.CheckAsync(
            "1.0.0", TestContext.Current.CancellationToken);

        Assert.True(info.IsAvailable);
        Assert.Equal("1.0.0", info.CurrentVersion);
        Assert.Equal("1.2.0", info.LatestVersion);
        Assert.Equal("https://github.com/example/repo/app.zip", info.DownloadUrl);
        Assert.Equal("Release notes", info.ReleaseNotes);
        Assert.Contains("api.github.com/repos/example/repo/releases/latest",
            requested!.ToString());
        Assert.Contains("application/vnd.github+json", accept);
    }

    [Theory]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("v1.0.0-beta", "1.0.0", false)]
    [InlineData("1.0", "1.0.1", false)]
    public void IsNewer_ComparesVersionSegments(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(candidate, current));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] BuildServersDat(string name, string ip)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)10);
            WriteNbtString(writer, "");
            writer.Write((byte)9);
            WriteNbtString(writer, "servers");
            writer.Write((byte)10);
            writer.Write(1);

            writer.Write((byte)8);
            WriteNbtString(writer, "name");
            WriteNbtString(writer, name);

            writer.Write((byte)8);
            WriteNbtString(writer, "ip");
            WriteNbtString(writer, ip);

            writer.Write((byte)0);
            writer.Write((byte)0);
        }

        return output.ToArray();
    }

    private static void WriteNbtString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
