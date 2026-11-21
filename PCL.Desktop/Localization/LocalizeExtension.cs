using Avalonia.Markup.Xaml;

namespace PCL.Desktop.Localization;

/// <summary>用于 XAML 静态文案的本地化标记扩展。</summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Current[Key];
}

/// <summary>集中持有当前字符串目录，供 XAML 与界面共用。</summary>
public static class Loc
{
    private static LocalizationService? _current;

    public static LocalizationService Current => _current ??= new LocalizationService();

    public static void Initialize(LocalizationService service) => _current = service;
}
