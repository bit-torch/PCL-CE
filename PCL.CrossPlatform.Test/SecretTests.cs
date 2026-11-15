using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Utils.Secret;

namespace PCL.CrossPlatform.Test;

[TestClass]
[DoNotParallelize]
public class SecretTests
{
    [TestMethod]
    public void UnixSecretsSurviveRestartAndUseOwnerOnlyPermissions()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("This test exercises Unix-specific behavior.");
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "UserKey.unix.bin");
        EncryptHelper.OverrideKeyFile(path); EncryptHelper.ResetKeyCache();
        try
        {
            var encrypted = EncryptHelper.SecretEncrypt("test refresh token");
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            EncryptHelper.ResetKeyCache();
            Assert.AreEqual("test refresh token", EncryptHelper.SecretDecrypt(encrypted));
            var original = File.ReadAllBytes(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
            EncryptHelper.ResetKeyCache();
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => EncryptHelper.EncryptionKey);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.AreEqual(1, Directory.GetFiles(temp.Path).Length, "Insecure access must not silently rotate the key.");
        }
        finally { EncryptHelper.OverrideKeyFile(null); EncryptHelper.ResetKeyCache(); }
    }
}
