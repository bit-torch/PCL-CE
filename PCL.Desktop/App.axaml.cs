using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using PCL.Desktop.Dialogs;
using PCL.Desktop.Services;
using PCL.Desktop.ViewModels;

namespace PCL.Desktop;

public partial class App : Application
{
    private LauncherServices? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new LauncherServices();
            _services = services;
            AppearanceService.Apply(services.Settings);
            RequestedThemeVariant = services.Settings.DarkTheme
                ? ThemeVariant.Dark
                : ThemeVariant.Light;

            var window = new MainWindow();
            var assembly = typeof(App).Assembly.GetName().Name;
            window.Icon = new WindowIcon(AssetLoader.Open(
                new Uri($"avares://{assembly}/Assets/app-icon.png")));

            var viewModel = new MainWindowViewModel(services, async () =>
            {
                var dialog = new LicensePrecheckWindow(services.Localization);
                return await dialog.ShowDialog<bool>(window);
            });

            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
