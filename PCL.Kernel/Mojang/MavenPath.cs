namespace PCL.Kernel.Mojang;

public static class MavenPath
{
    /// <summary>
    /// 将 group:artifact:version[:classifier][@extension] 转换为 Maven 仓库相对路径。
    /// </summary>
    public static string FromCoordinate(string coordinate)
    {
        if (string.IsNullOrWhiteSpace(coordinate))
            throw new ArgumentException("Maven 坐标不能为空。", nameof(coordinate));

        var extension = "jar";
        var at = coordinate.IndexOf('@');
        if (at >= 0)
        {
            extension = coordinate[(at + 1)..];
            coordinate = coordinate[..at];
        }

        var parts = coordinate.Split(':');
        if (parts.Length < 3)
            return coordinate.Replace(':', '/') + "." + extension;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}";
    }
}
