using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;

namespace PCL.Desktop.Localization;

/// <summary>
/// 新 UI 的独立字符串目录。资源内嵌在程序集中，不读取 PCL 的语言文件。
/// </summary>
public sealed class LocalizationService
{
    private const string FallbackLanguage = "zh-CN";
    private const string EnglishLanguage = "en-US";

    private readonly Dictionary<string, string> _fallback;
    private readonly Dictionary<string, string> _current;

    public LocalizationService(string? language = null)
    {
        Language = Normalize(language ?? CultureInfo.CurrentUICulture.Name);
        _fallback = Load(FallbackLanguage);
        _current = Language == FallbackLanguage ? _fallback : Load(Language);
    }

    public string Language { get; }

    public string this[string key] =>
        _current.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out var fallback) ? fallback
        : key;

    private static string Normalize(string name)
    {
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return FallbackLanguage;

        return EnglishLanguage;
    }

    private static Dictionary<string, string> Load(string language)
    {
        var assembly = typeof(LocalizationService).Assembly.GetName().Name;
        var uri = new Uri($"avares://{assembly}/Localization/Strings.{language}.json");
        using var stream = AssetLoader.Open(uri);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? new Dictionary<string, string>();
    }
}
