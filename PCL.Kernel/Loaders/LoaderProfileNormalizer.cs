using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Kernel.Mojang;

namespace PCL.Kernel.Loaders;

/// <summary>
/// 将 Fabric / Quilt 的 Maven 风格依赖补全为 Mojang 的 downloads.artifact 形式，
/// 这样安装器与启动器可以复用同一条依赖处理链路。
/// </summary>
public static class LoaderProfileNormalizer
{
    public static string Normalize(string profileJson, string fallbackMavenUrl)
    {
        var root = JsonNode.Parse(profileJson)?.AsObject()
                   ?? throw new InvalidDataException("无法解析加载器版本 JSON。");

        if (root["libraries"] is JsonArray libraries)
        {
            foreach (var node in libraries)
            {
                if (node is not JsonObject library)
                    continue;

                if (library["downloads"] is JsonObject downloads &&
                    downloads["artifact"] is JsonObject)
                    continue;

                var name = library["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string relativePath;
                try
                {
                    relativePath = MavenPath.FromCoordinate(name);
                }
                catch (Exception)
                {
                    continue;
                }

                var mavenBase = library["url"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(mavenBase))
                    mavenBase = fallbackMavenUrl;

                var artifact = new JsonObject
                {
                    ["path"] = relativePath,
                    ["url"] = mavenBase.TrimEnd('/') + "/" + relativePath
                };

                var sha1 = library["sha1"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(sha1))
                    artifact["sha1"] = sha1;

                if (library["size"] is JsonValue sizeValue &&
                    sizeValue.TryGetValue<long>(out var size) &&
                    size > 0)
                    artifact["size"] = size;

                library["downloads"] = new JsonObject { ["artifact"] = artifact };
            }
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
