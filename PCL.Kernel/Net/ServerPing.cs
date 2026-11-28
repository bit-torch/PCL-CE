using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCL.Kernel.Net;

/// <summary>一次 Minecraft 服务器状态探测的结果。</summary>
public sealed record ServerStatus(
    bool IsReachable,
    string Motd,
    int OnlinePlayers,
    int MaxPlayers,
    string VersionName,
    int LatencyMs,
    string? Error)
{
    public static ServerStatus Failure(string error) =>
        new(false, "", 0, 0, "", 0, error);
}

/// <summary>
/// Minecraft Java 版服务器列表探测（Server List Ping）。
/// 只读取状态响应，不发送登录请求，也不会向服务器写入任何账号信息。
/// </summary>
public static class ServerPing
{
    private const int DefaultPort = 25565;
    private const int MaxResponseBytes = 8 * 1024 * 1024;

    /// <summary>解析 "host"、"host:port" 或 "[ipv6]:port" 形式的地址。</summary>
    public static (string Host, int Port) ParseAddress(string address)
    {
        address = (address ?? "").Trim();
        if (address.Length == 0)
            throw new ArgumentException("服务器地址不能为空。", nameof(address));

        if (address.StartsWith('['))
        {
            var end = address.IndexOf(']');
            if (end > 0)
            {
                var host = address[1..end];
                var rest = address[(end + 1)..];
                var port = rest.StartsWith(':') && int.TryParse(rest[1..], out var parsed)
                    ? parsed
                    : DefaultPort;
                return (host, port);
            }
        }

        var colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out var explicitPort))
            return (address[..colon], explicitPort);

        return (address, DefaultPort);
    }

    public static async Task<ServerStatus> PingAsync(
        string address,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        string host;
        int port;
        try
        {
            (host, port) = ParseAddress(address);
        }
        catch (Exception ex)
        {
            return ServerStatus.Failure(ex.Message);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1000, timeoutMs));
        var token = timeout.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            await WriteHandshakeAsync(stream, host, port, token).ConfigureAwait(false);

            // 状态请求：包长 1，包 ID 0x00
            await stream.WriteAsync(new byte[] { 0x01, 0x00 }, token).ConfigureAwait(false);

            var length = await ReadVarIntAsync(stream, token).ConfigureAwait(false);
            if (length <= 0 || length > MaxResponseBytes)
                return ServerStatus.Failure("服务器返回了无效的响应长度。");

            var payload = new byte[length];
            await ReadExactAsync(stream, payload, token).ConfigureAwait(false);
            stopwatch.Stop();

            var (packetId, offset) = ReadVarInt(payload, 0);
            if (packetId != 0x00)
                return ServerStatus.Failure("服务器返回了非状态响应。");

            var (jsonLength, jsonOffset) = ReadVarInt(payload, offset);
            if (jsonLength <= 0 || jsonLength > payload.Length - jsonOffset)
                return ServerStatus.Failure("服务器返回了无效的 JSON 长度。");

            var json = Encoding.UTF8.GetString(payload, jsonOffset, jsonLength);
            return ParseStatus(json, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ServerStatus.Failure("连接超时。");
        }
        catch (Exception ex)
        {
            return ServerStatus.Failure(ex.Message);
        }
    }

    private static async Task WriteHandshakeAsync(
        NetworkStream stream, string host, int port, CancellationToken token)
    {
        using var payload = new MemoryStream();
        WriteVarInt(payload, 0x00);
        WriteVarInt(payload, -1);
        WriteString(payload, host);
        payload.WriteByte((byte)(port >> 8));
        payload.WriteByte((byte)(port & 0xFF));
        WriteVarInt(payload, 1);

        using var packet = new MemoryStream();
        WriteVarInt(packet, (int)payload.Length);
        payload.Position = 0;
        payload.CopyTo(packet);

        await stream.WriteAsync(packet.ToArray(), token).ConfigureAwait(false);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken token)
    {
        var result = 0;
        var shift = 0;
        var buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("服务器提前关闭了连接。");

            var value = buffer[0];
            result |= (value & 0x7F) << shift;
            if ((value & 0x80) == 0)
                return result;

            shift += 7;
            if (shift > 35)
                throw new InvalidDataException("VarInt 过长。");
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("服务器提前关闭了连接。");
            offset += read;
        }
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var remaining = unchecked((uint)value);
        do
        {
            var current = (byte)(remaining & 0x7F);
            remaining >>= 7;
            if (remaining != 0)
                current |= 0x80;
            stream.WriteByte(current);
        } while (remaining != 0);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static (int Value, int Offset) ReadVarInt(byte[] buffer, int offset)
    {
        var result = 0;
        var shift = 0;
        while (offset < buffer.Length)
        {
            var value = buffer[offset++];
            result |= (value & 0x7F) << shift;
            if ((value & 0x80) == 0)
                return (result, offset);
            shift += 7;
            if (shift > 35)
                break;
        }

        throw new InvalidDataException("响应中的 VarInt 无效。");
    }

    private static ServerStatus ParseStatus(string json, int latency)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var motd = root.TryGetProperty("description", out var description)
            ? FlattenDescription(description)
            : "";

        var online = 0;
        var max = 0;
        if (root.TryGetProperty("players", out var players))
        {
            if (players.TryGetProperty("online", out var onlineElement) &&
                onlineElement.TryGetInt32(out var onlineValue))
            {
                online = onlineValue;
            }

            if (players.TryGetProperty("max", out var maxElement) &&
                maxElement.TryGetInt32(out var maxValue))
            {
                max = maxValue;
            }
        }

        var version = "";
        if (root.TryGetProperty("version", out var versionElement) &&
            versionElement.TryGetProperty("name", out var nameElement))
        {
            version = nameElement.GetString() ?? "";
        }

        return new ServerStatus(true, motd, online, max, version, latency, null);
    }

    private static string FlattenDescription(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return StripCodes(element.GetString() ?? "");

        if (element.ValueKind != JsonValueKind.Object)
            return "";

        var builder = new StringBuilder();
        AppendComponent(element, builder);
        return StripCodes(builder.ToString()).Trim();
    }

    private static void AppendComponent(JsonElement element, StringBuilder builder)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            builder.Append(element.GetString());
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("text", out var text) &&
            text.ValueKind == JsonValueKind.String)
        {
            builder.Append(text.GetString());
        }

        if (element.TryGetProperty("translate", out var translate) &&
            translate.ValueKind == JsonValueKind.String)
        {
            builder.Append(translate.GetString());
        }

        if (element.TryGetProperty("extra", out var extra) &&
            extra.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in extra.EnumerateArray())
                AppendComponent(child, builder);
        }
    }

    private static string StripCodes(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\u00A7') < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\u00A7')
            {
                index++;
                continue;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }
}
