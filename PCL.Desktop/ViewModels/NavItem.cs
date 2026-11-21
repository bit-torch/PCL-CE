using Avalonia.Media;

namespace PCL.Desktop.ViewModels;

public sealed record NavItem(string Key, string Label, Geometry Icon)
{
    public static IReadOnlyList<NavItem> CreateDefault() =>
    [
        new("home", "总览", StreamGeometry.Parse("M3 10.5 12 3l9 7.5V21H3z M9 21v-7h6v7")),
        new("install", "安装", StreamGeometry.Parse("M12 3v12 M7 10l5 5 5-5 M4 21h16")),
        new("launch", "启动", StreamGeometry.Parse("M8 5v14l11-7z")),
        new("settings", "设置", StreamGeometry.Parse(
            "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M12 2v3 M12 19v3 M2 12h3 M19 12h3 " +
            "M4.9 4.9 7 7 M17 17l2.1 2.1 M19.1 4.9 17 7 M7 17l-2.1 2.1")),
        new("about", "关于", StreamGeometry.Parse(
            "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M12 11v6 M12 7.5v.5"))
    ];
}
