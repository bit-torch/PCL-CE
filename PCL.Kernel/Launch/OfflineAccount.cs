using System.Security.Cryptography;
using System.Text;

namespace PCL.Kernel.Launch;

public static class OfflineAccount
{
    /// <summary>
    /// 生成与 Java UUID.nameUUIDFromBytes("OfflinePlayer:name") 一致的版本 3 UUID。
    /// </summary>
    public static string CreateUuid(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("用户名不能为空。", nameof(username));

        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
