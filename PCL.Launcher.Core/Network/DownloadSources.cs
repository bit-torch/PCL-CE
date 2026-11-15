using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;

namespace PCL;

/// <summary>下载源排序与镜像地址生成，供业务模块共享。</summary>
public static class DownloadSources
{
    /// <summary>当前使用的自定义镜像源基址；未配置时为空字符串。</summary>
    public static string MirrorBase
    {
        get
        {
            var custom = (Config.Download.CustomSource ?? "").Trim();
            if (string.IsNullOrEmpty(custom))
                return "";
            if (!custom.Contains("://"))
                custom = "https://" + custom;
            return custom.TrimEnd('/');
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