using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Avalonia;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Models;
using PCL.Core.Utils.Secret;
using PCL.CrossPlatform;

namespace PCL.CrossPlatform.Test;

[TestClass]
[DoNotParallelize]
public class RuntimeAndUpdateTests
{
    [TestMethod]
    public async Task JavaInstallerVerifiesDigestAndPreservesExecutableMode()
    {
        using var temp = new TemporaryDirectory(); var handler = new FixtureHttp(); using var client = new HttpClient(handler);
        var name = OperatingSystem.IsWindows() ? "jdk/bin/java.exe" : "jdk/bin/java";
        var archive = OperatingSystem.IsWindows() ? Zip(name, "java") : Tar((name, "java"));
        handler.Files["https://files.test/jdk"] = archive;
        handler.Json("https://api.adoptium.net/*", new JsonArray(new JsonObject
        {
            ["binary"] = new JsonObject { ["package"] = new JsonObject { ["link"] = "https://files.test/jdk", ["size"] = archive.Length, ["checksum"] = Convert.ToHexString(SHA256.HashData(archive)) } }
        }));
        var path = await new JavaRuntimeInstaller(client).InstallAsync(21, temp.Path, null, default);
        Assert.AreEqual("java", File.ReadAllText(path));
        if (!OperatingSystem.IsWindows()) Assert.IsTrue((File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0);
        var second = await new JavaRuntimeInstaller(client).InstallAsync(21, temp.Path, null, default); Assert.AreEqual(path, second);
        Assert.AreEqual(1, handler.Requests.Count(url => url == "https://files.test/jdk"));
    }

    [TestMethod]
    public async Task BadJavaDigestDoesNotPublishRuntime()
    {
        using var temp = new TemporaryDirectory(); var handler = new FixtureHttp(); using var client = new HttpClient(handler);
        handler.Files["https://files.test/jdk"] = [1, 2, 3];
        handler.Json("https://api.adoptium.net/*", """[{"binary":{"package":{"link":"https://files.test/jdk","size":3,"checksum":"0000000000000000000000000000000000000000000000000000000000000000"}}}]""");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new JavaRuntimeInstaller(client).InstallAsync(21, temp.Path, null, default));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(temp.Path).Length);
    }

    [TestMethod]
    public async Task UpdateStagingSeparatesHelperAndBackupFromPayload()
    {
        using var temp = new TemporaryDirectory(); var executable = OperatingSystem.IsWindows() ? "PCL-CE.exe" : "PCL-CE";
        var archive = Path.Combine(temp.Path, "update.tar.gz"); File.WriteAllBytes(archive, Tar((executable, "program"), ("PCL-CE.dll", "assembly")));
        var plan = await DesktopUpdater.PrepareAsync(archive, Path.Combine(temp.Path, "install with spaces"), temp.Path, default);
        Assert.AreNotEqual(plan.SourceDirectory, plan.WorkingDirectory);
        var info = DesktopUpdater.CreateApplyProcess(plan, 12345);
        Assert.Contains(plan.SourceDirectory, info.ArgumentList.ToArray()); Assert.Contains(plan.InstallDirectory, info.ArgumentList.ToArray());
        Assert.IsFalse(info.UseShellExecute); Assert.AreEqual("", info.Arguments);
        Assert.IsTrue(File.Exists(Path.Combine(plan.SourceDirectory, executable)));
        Assert.IsFalse(Directory.GetFiles(plan.SourceDirectory).Any(path => path.EndsWith(".ps1") || path.EndsWith(".sh")));
    }

    [TestMethod]
    public async Task UpdateArchiveRejectsTraversalAndMissingProgram()
    {
        using var temp = new TemporaryDirectory(); var archive = Path.Combine(temp.Path, "update.tar.gz");
        File.WriteAllBytes(archive, Tar(("../outside", "bad")));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DesktopUpdater.PrepareAsync(archive, "/unused", temp.Path, default));
        Assert.IsFalse(File.Exists(Path.Combine(temp.Path, "outside")));
        File.WriteAllBytes(archive, Tar(("unrelated", "file")));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DesktopUpdater.PrepareAsync(archive, "/unused", temp.Path, default));
        Assert.AreEqual(0, Directory.GetDirectories(temp.Path, "update-*").Length);
    }

    [TestMethod]
    public async Task UnixUpdateHelperBacksUpProgramAndRestartsOnlyPreparedExecutable()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix update helper integration test."); return; }
        using var temp = new TemporaryDirectory(); var install = Path.Combine(temp.Path, "install with spaces"); Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "old-file"), "old data");
        var archive = Path.Combine(temp.Path, "update.tar.gz");
        File.WriteAllBytes(archive, Tar(("PCL-CE", "#!/bin/sh\nprintf updated > \"$(dirname \"$0\")/restarted\"\n"), ("PCL-CE.dll", "assembly")));
        var plan = await DesktopUpdater.PrepareAsync(archive, install, temp.Path, default);
        using var process = Process.Start(DesktopUpdater.CreateApplyProcess(plan, 999999999))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token);
        Assert.AreEqual(0, process.ExitCode);
        for (var attempt = 0; attempt < 30 && !File.Exists(Path.Combine(install, "restarted")); attempt++) await Task.Delay(25, timeout.Token);
        Assert.AreEqual("updated", File.ReadAllText(Path.Combine(install, "restarted")));
        Assert.AreEqual("old data", File.ReadAllText(Path.Combine(plan.WorkingDirectory, "previous", "old-file")));
        Assert.IsFalse(Directory.Exists(Path.Combine(install, "previous")), "Backup must never be copied into the payload.");
    }

    [TestMethod]
    public void MultipleAccountsMigrateLegacyStorageAndRefreshWithoutDuplicates()
    {
        using var temp = new TemporaryDirectory(); EncryptHelper.OverrideKeyFile(Path.Combine(temp.Path, "key.bin")); EncryptHelper.ResetKeyCache();
        try
        {
            var old = new McProfile { ProfileId = "old", ProfileType = ProfileType.Microsoft, UserName = "Old", Uuid = "uuid-old", AccessToken = "old-token" };
            var settings = new DesktopSettings { EncryptedAccount = EncryptHelper.SecretEncrypt(System.Text.Json.JsonSerializer.Serialize(old)) };
            Assert.AreEqual("Old", settings.SelectedAccount()!.UserName);
            settings.StoreAccount(new AuthenticationResult { ProfileType = ProfileType.Authlib, UserName = "New", Uuid = "uuid-new", AccessToken = "secret-token", Server = "https://auth.test/authserver" });
            Assert.AreEqual(2, settings.Accounts().Count); Assert.AreEqual("New", settings.SelectedAccount()!.UserName);
            settings.StoreAccount(new AuthenticationResult { ProfileType = ProfileType.Authlib, UserName = "New", Uuid = "uuid-new", AccessToken = "refreshed", Server = "https://auth.test/authserver" });
            Assert.AreEqual(2, settings.Accounts().Count); Assert.AreEqual("refreshed", settings.SelectedAccount()!.AccessToken);
            Assert.IsFalse(settings.EncryptedAccounts!.Contains("refreshed", StringComparison.Ordinal));
            settings.RemoveSelectedAccount(); Assert.AreEqual("Old", settings.SelectedAccount()!.UserName);
        }
        finally { EncryptHelper.OverrideKeyFile(null); EncryptHelper.ResetKeyCache(); }
    }

    private static byte[] Zip(string name, string content)
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(content);
        return stream.ToArray();
    }
    private static byte[] Tar(params (string Name, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Compress, true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        foreach (var (name, content) in files)
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data, Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute });
        }
        return stream.ToArray();
    }
}
