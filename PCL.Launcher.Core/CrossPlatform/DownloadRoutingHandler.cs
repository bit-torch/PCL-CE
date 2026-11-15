using System.Net.Http;

namespace PCL.CrossPlatform;

/// <summary>Applies an explicitly configured BMCLAPI-compatible mirror to public game downloads only.</summary>
public sealed class DownloadRoutingHandler(Func<string?> mirror, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public static Uri Map(Uri source, string? mirror)
    {
        if (string.IsNullOrWhiteSpace(mirror)) return source;
        if (!Uri.TryCreate(mirror.TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme != "https")
            throw new ArgumentException("下载镜像必须为 HTTPS 地址。");
        var prefix = source.Host switch
        {
            "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" or "launcher.mojang.com" => "",
            "resources.download.minecraft.net" => "/assets",
            "libraries.minecraft.net" => "/maven",
            "meta.fabricmc.net" => "/fabric-meta",
            "meta.legacyfabric.net" => "/legacyfabric-meta",
            _ => null
        };
        return prefix is null ? source : new Uri(root.AbsoluteUri.TrimEnd('/') + prefix + source.PathAndQuery);
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri is { } uri) request.RequestUri = Map(uri, mirror());
        return base.SendAsync(request, cancellationToken);
    }
}
