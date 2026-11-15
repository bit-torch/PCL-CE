using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;

namespace PCL;

/// <summary>下载源排序与镜像地址生成，供业务模块共享。</summary>
public static class DownloadSources
{
    /// <summary>当前已添加的自定义镜像源基址列表。</summary>
    public static IReadOnlyList<string> MirrorBases
    {
        get
        {
            var raw = Config.Download.CustomSource ?? "";
            if (raw.Length == 0)
                return [];

            List<string> result = [];
            foreach (var part in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = NormalizeSource(part);
                if (normalized.Length == 0 || !IsValidSource(normalized))
                    continue;
                if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    result.Add(normalized);
            }
            return result;
        }
    }

    /// <summary>当前使用的自定义下载源序号；未添加时返回 0。</summary>
    public static int ActiveIndex
    {
        get
        {
            var count = MirrorBases.Count;
            return count == 0 ? 0 : Math.Clamp(Config.Download.CustomSourceIndex, 0, count - 1);
        }
    }

    /// <summary>当前使用的自定义镜像源基址；未配置时为空字符串。</summary>
    public static string MirrorBase
    {
        get
        {
            var bases = MirrorBases;
            return bases.Count == 0 ? "" : bases[ActiveIndex];
        }
    }

    /// <summary>是否已在设置中手动添加下载源。</summary>
    public static bool HasCustomSource => MirrorBase.Length > 0;

    /// <summary>校验是否已手动添加下载源。未添加时显示提示并返回 <c>false</c>。</summary>
    public static bool EnsureConfigured()
    {
        if (HasCustomSource) return true;
        HintService.Show(Hints.Error(Lang.Text("Download.Error.CustomSourceRequired")));
        return false;
    }

    /// <summary>规范化下载源地址：去除首尾空白、补全协议、移除末尾斜杠。</summary>
    public static string NormalizeSource(string? source)
    {
        var value = (source ?? "").Trim();
        if (value.Length == 0)
            return "";
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        return value.TrimEnd('/');
    }

    /// <summary>判断下载源是否为有效的 HTTP(S) 地址。</summary>
    public static bool IsValidSource(string? source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>保存下载源列表与当前选中的序号。</summary>
    public static void SaveSources(IReadOnlyList<string> sources, int activeIndex = 0)
    {
        List<string> normalized = [];
        foreach (var source in sources)
        {
            var value = NormalizeSource(source);
            if (value.Length == 0 || !IsValidSource(value))
                continue;
            if (!normalized.Contains(value, StringComparer.OrdinalIgnoreCase))
                normalized.Add(value);
        }

        Config.Download.CustomSource = string.Join('\n', normalized);
        Config.Download.CustomSourceIndex = normalized.Count == 0
            ? 0
            : Math.Clamp(activeIndex, 0, normalized.Count - 1);
    }

    /// <summary>下载文件时是否优先使用官方源。</summary>
    public static bool PreferMojang { get; set; }

    public static IEnumerable<string> Order(
        IEnumerable<string> officialUrls,
        IEnumerable<string> mirrorUrls)
    {
        return PreferMojang
            ? officialUrls.Union(mirrorUrls)
            : mirrorUrls.Union(officialUrls);
    }

    /// <summary>读取 Forge 推荐版本缓存。</summary>
    public static string? ForgeRecommendedGet(string? mcInstance)
    {
        try
        {
            if (mcInstance is null)
                return null;
            var list = LauncherCommon.ReadFile(
                Path.Combine(LauncherPaths.Temp, "Cache", "ForgeRecommendedList.json"));
            if (string.IsNullOrEmpty(list))
            {
                LauncherCommon.Log("[Download] 没有 Forge 推荐版本缓存文件");
                return null;
            }

            var json = (JsonObject)LauncherCommon.GetJson(list);
            if (!mcInstance.Contains(".") || !json.ContainsKey(mcInstance))
                return null;
            return (json[mcInstance] ?? "").ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>获取 Assets 文件的下载地址列表。</summary>
    public static IEnumerable<string> AssetsGet(string original)
    {
        original = original.Replace(
            "http://resources.download.minecraft.net",
            "https://resources.download.minecraft.net");
        return Order(
            [original],
            [
                original
                    .Replace(
                        "https://piston-data.mojang.com",
                        $"{MirrorBase}/assets")
                    .Replace(
                        "https://piston-meta.mojang.com",
                        $"{MirrorBase}/assets")
                    .Replace(
                        "https://resources.download.minecraft.net",
                        $"{MirrorBase}/assets")
            ]);
    }
}