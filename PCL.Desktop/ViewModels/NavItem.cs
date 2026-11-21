using Avalonia.Media;
using PCL.Desktop.Assets;
using PCL.Desktop.Localization;

namespace PCL.Desktop.ViewModels;

public sealed record NavItem(string Key, string Label, Geometry Icon)
{
    public static IReadOnlyList<NavItem> CreateDefault(LocalizationService strings) =>
    [
        new("home", strings["nav.home"], Icons.Home),
        new("install", strings["nav.install"], Icons.Install),
        new("resources", strings["nav.resources"], Icons.Resources),
        new("launch", strings["nav.launch"], Icons.Launch),
        new("settings", strings["nav.settings"], Icons.Settings),
        new("about", strings["nav.about"], Icons.About)
    ];
}
