using System.IO.Compression;
using System.Text;

namespace PCL.Kernel.GameData;

/// <summary>读取 servers.dat 所需的最小 NBT 解析器，只支持标准标签类型。</summary>
internal static class NbtReader
{
    public static object? Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var type = reader.ReadByte();
        if (type == 0)
            return null;

        _ = ReadString(reader);
        return ReadPayload(reader, type);
    }

    private static object? ReadPayload(BinaryReader reader, byte type) => type switch
    {
        1 => reader.ReadSByte(),
        2 => reader.ReadInt16(),
        3 => reader.ReadInt32(),
        4 => reader.ReadInt64(),
        5 => reader.ReadSingle(),
        6 => reader.ReadDouble(),
        7 => ReadByteArray(reader),
        8 => ReadString(reader),
        9 => ReadList(reader),
        10 => ReadCompound(reader),
        11 => ReadIntArray(reader),
        12 => ReadLongArray(reader),
        _ => throw new InvalidDataException($"未知的 NBT 标签类型：{type}")
    };

    private static byte[] ReadByteArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return length <= 0 ? [] : reader.ReadBytes(length);
    }

    private static int[] ReadIntArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length <= 0)
            return [];

        var values = new int[length];
        for (var index = 0; index < length; index++)
            values[index] = reader.ReadInt32();

        return values;
    }

    private static long[] ReadLongArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length <= 0)
            return [];

        var values = new long[length];
        for (var index = 0; index < length; index++)
            values[index] = reader.ReadInt64();

        return values;
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        return length == 0 ? "" : Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static Dictionary<string, object?> ReadCompound(BinaryReader reader)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        while (true)
        {
            var type = reader.ReadByte();
            if (type == 0)
                break;

            var name = ReadString(reader);
            values[name] = ReadPayload(reader, type);
        }

        return values;
    }

    private static List<object?> ReadList(BinaryReader reader)
    {
        var type = reader.ReadByte();
        var length = reader.ReadInt32();
        var values = new List<object?>(Math.Max(0, length));

        for (var index = 0; index < length; index++)
            values.Add(ReadPayload(reader, type));

        return values;
    }
}

/// <summary>读取 servers.dat（gzip 压缩的 NBT）中的服务器列表。</summary>
public static class ServerList
{
    public static IReadOnlyList<ServerEntry> Read(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, "servers.dat");
        if (!File.Exists(path))
            return [];

        try
        {
            return Parse(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            return [];
        }
    }

    /// <summary>解析已读入内存的 servers.dat，便于测试与非文件来源复用。</summary>
    public static IReadOnlyList<ServerEntry> Parse(byte[] data)
    {
        using var source = new MemoryStream(data, writable: false);
        using var stream = IsGzip(data)
            ? (Stream)new GZipStream(source, CompressionMode.Decompress)
            : source;

        if (NbtReader.Read(stream) is not Dictionary<string, object?> root ||
            root.GetValueOrDefault("servers") is not List<object?> servers)
            return [];

        return servers
            .OfType<Dictionary<string, object?>>()
            .Select(server => new ServerEntry(
                server.GetValueOrDefault("name") as string ?? "",
                server.GetValueOrDefault("ip") as string ?? "",
                server.GetValueOrDefault("icon") as string))
            .Where(server => server.Ip.Length > 0)
            .ToArray();
    }

    private static bool IsGzip(byte[] data) =>
        data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b;
}
