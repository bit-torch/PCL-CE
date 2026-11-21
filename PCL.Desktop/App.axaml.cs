using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            _services = new LauncherServices();

            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(_services, async () =>
            {
                var dialog = new LicensePrecheckWindow();
                return await dialog.ShowDialog<bool>(window);
            });

            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
