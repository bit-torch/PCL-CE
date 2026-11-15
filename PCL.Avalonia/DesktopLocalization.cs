using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Avalonia.Threading;
using PCL.Core.App.Localization;
using PCL.Core.Platform;

namespace PCL.Avalonia;

/// <summary>Reads the existing language dictionaries as plain XML strings, without loading WPF XAML.</summary>
public sealed class DesktopLocalization : ILocalizationBackend
{
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _phrases = new(StringComparer.Ordinal);
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);
    public string? TryFindResource(string key) => _strings.GetValueOrDefault(key);
    public string Translate(string text) => _phrases.GetValueOrDefault(text) ?? text;
    public void ApplyLanguageResources(string baseLanguageCode, string? currentLanguageCode)
    {
        var chinese = Read("zh-CN"); _strings.Clear(); foreach (var pair in Read(baseLanguageCode)) _strings[pair.Key] = pair.Value;
        if (!string.IsNullOrWhiteSpace(currentLanguageCode)) foreach (var pair in Read(currentLanguageCode)) _strings[pair.Key] = pair.Value;
        _phrases.Clear(); foreach (var (key, value) in chinese) if (_strings.TryGetValue(key, out var translation)) _phrases.TryAdd(value, translation);
        var language = currentLanguageCode ?? baseLanguageCode;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
    }
    public void ApplyLaunchFont(string? customFontName, LocalizationLanguage? language) { }
    private static Dictionary<string, string> Read(string language)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL.Languages." + language + ".xml");
        if (input is null) return [];
        return XDocument.Load(input).Root!.Elements().Where(element => element.Name.LocalName == "String")
            .Where(element => element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key") is not null)
            .ToDictionary(element => element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key")!.Value, element => element.Value);
    }
}
