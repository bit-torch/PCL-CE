using System.IO.Compression;
using PCL.Kernel.App;
using PCL.Kernel.GameData;
using PCL.Kernel.Net;
using PCL.Kernel.Versions;
using Xunit;

namespace PCL.Tests;

public sealed class NecessaryFeaturesTests
{
    [Fact]
    public void MirrorUrl_MapsMojangHosts()
    {
        const string mirror = "https://bmclapi2.bangbang93.com/";

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
            DownloadClient.ResolveMirrorUrl(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", mirror));

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/assets/ab/cdef",
            DownloadClient.ResolveMirrorUrl(
                "https://resources.download.minecraft.net/ab/cdef", mirror));

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/com/example/a.jar",
            DownloadClient.ResolveMirrorUrl(
                "https://libraries.minecraft.net/com/example/a.jar", mirror));

        Assert.Null(DownloadClient.ResolveMirrorUrl("https://example.com/file", mirror));
        Assert.Null(DownloadClient.ResolveMirrorUrl(
            "https://piston-meta.mojang.com/x", ""));
    }

    [Fact]
    public void Settings_Normalize_ClampsDownloadAndWindow()
    {
        var settings = new LauncherSettings
        {
            Download = new DownloadSettings
            {
                Mirror = "  https://mirror.example/  ",
                ThreadLimit = 999,
                SpeedLimitKb = -10
            },
            GameWindowWidth = -100,
            GameWindowHeight = 999999
        };

        settings.Normalize();

        Assert.Equal("https://mirror.example", settings.Download.Mirror);
        Assert.Equal(64, settings.Download.ThreadLimit);
        Assert.Equal(0, settings.Download.SpeedLimitKb);
        Assert.Equal(0, settings.GameWindowWidth);
        Assert.Equal(16384, settings.GameWindowHeight);
    }

    [Fact]
    public async Task InstanceExporter_WritesVersionAndGameFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var versionDirectory = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(versionDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, "1.20.1.json"), "{}",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, "1.20.1.jar"), "jar",
                TestContext.Current.CancellationToken);

            var gameDirectory = Path.Combine(root, "game");
            Directory.CreateDirectory(gameDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(gameDirectory, "options.txt"), "lang:zh_cn",
                TestContext.Current.CancellationToken);

            var version = new InstalledVersion(
                "1.20.1", versionDirectory,
                Path.Combine(versionDirectory, "1.20.1.json"),
                Path.Combine(versionDirectory, "1.20.1.jar"),
                "Vanilla", null);

            var destination = Path.Combine(root, "export.zip");
            var result = await InstanceExporter.ExportAsync(
                version, destination, gameDirectory,
                TestContext.Current.CancellationToken);

            Assert.Equal(3, result.FileCount);
            Assert.True(File.Exists(destination));

            using var archive = ZipFile.OpenRead(destination);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("1.20.1/1.20.1.json", names);
            Assert.Contains("1.20.1/1.20.1.jar", names);
            Assert.Contains("1.20.1/game/options.txt", names);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InstanceExporter_RejectsDestinationInsideVersion()
    {
        var root = CreateTempDirectory();
        try
        {
            var versionDirectory = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(versionDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, "1.20.1.json"), "{}",
                TestContext.Current.CancellationToken);

            var version = new InstalledVersion(
                "1.20.1", versionDirectory,
                Path.Combine(versionDirectory, "1.20.1.json"),
                null, "Vanilla", null);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InstanceExporter.ExportAsync(
                    version,
                    Path.Combine(versionDirectory, "inside.zip"),
                    cancellationToken: TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LogScanner_FindsLogsAndReader_SupportsTailAndSearch()
    {
        var root = CreateTempDirectory();
        try
        {
            var gameDirectory = Path.Combine(root, "game");
            var isolated = Path.Combine(root, "versions", "1.20.1", "game");
            Directory.CreateDirectory(Path.Combine(gameDirectory, "logs"));
            Directory.CreateDirectory(Path.Combine(isolated, "logs"));

            var logPath = Path.Combine(gameDirectory, "logs", "latest.log");
            await File.WriteAllLinesAsync(
                logPath, ["line one", "ERROR boom", "line three"],
                TestContext.Current.CancellationToken);

            await File.WriteAllLinesAsync(
                Path.Combine(isolated, "logs", "latest.log"), ["isolated"],
                TestContext.Current.CancellationToken);

            var entries = LogScanner.Scan([gameDirectory, isolated]);
            Assert.Equal(2, entries.Count);

            var tail = await LogReader.ReadTailAsync(
                logPath, 2, TestContext.Current.CancellationToken);
            Assert.Equal(
                string.Join(Environment.NewLine, "ERROR boom", "line three"), tail);

            var filtered = await LogReader.SearchAsync(
                logPath, "error", 10, TestContext.Current.CancellationToken);
            Assert.Equal("ERROR boom", filtered);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "pcl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
