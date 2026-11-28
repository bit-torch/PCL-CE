using Avalonia;
using Avalonia.Media;
using PCL.Kernel.App;

namespace PCL.Desktop.Services;

/// <summary>
/// 把外观设置应用到 Avalonia 资源。主题色与字体写入 Application.Resources，
/// 视图中的 DynamicResource 引用会在运行时立即刷新。
/// </summary>
public static class AppearanceService
{
    public const string DefaultAccent = "#2F6BFF";

    private const string DefaultFontFamily =
        "Inter, Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC";

    public static void Apply(LauncherSettings settings)
    {
        ApplyAccent(settings.AccentColor);
        ApplyFont(settings.UIFontFamily);
    }

    public static void ApplyAccent(string? hex)
    {
        if (Application.Current is not { } application)
            return;

        var accent = TryParse(hex) ?? TryParse(DefaultAccent)!.Value;
        var hover = Mix(accent, Colors.Black, 0.12);
        var soft = Mix(accent, Colors.White, 0.88);

        application.Resources["AccentBrush"] = new SolidColorBrush(accent);
        application.Resources["AccentHoverBrush"] = new SolidColorBrush(hover);
        application.Resources["AccentSoftBrush"] = new SolidColorBrush(soft);
        application.Resources["BrandBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(accent, 0),
                new(Mix(accent, Colors.White, 0.22), 1)
            }
        };
    }

    public static void ApplyFont(string? family)
    {
        if (Application.Current is not { } application)
            return;

        var name = string.IsNullOrWhiteSpace(family) ? DefaultFontFamily : family.Trim();
        application.Resources["AppFontFamily"] = new FontFamily(name);
    }

    private static Color? TryParse(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Color.TryParse(value.Trim(), out var color)
            ? color
            : null;

    private static Color Mix(Color source, Color target, double weight)
    {
        weight = Math.Clamp(weight, 0, 1);
        return Color.FromArgb(
            source.A,
            Blend(source.R, target.R, weight),
            Blend(source.G, target.G, weight),
            Blend(source.B, target.B, weight));
    }

    private static byte Blend(byte source, byte target, double weight) =>
        (byte)Math.Clamp((int)Math.Round(source + (target - source) * weight), 0, 255);
}
