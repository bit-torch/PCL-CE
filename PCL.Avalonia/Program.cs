using Avalonia;

namespace PCL.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var identity = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        using var instance = new PCL.CrossPlatform.SingleInstanceHost("PCLCE-" + Convert.ToHexString(identity)[..24]);
        if (!instance.OwnsInstance)
        {
            try { instance.ActivatePrimaryAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { Console.Error.WriteLine("无法激活已运行的启动器：" + ex.Message); }
            return 0;
        }
        instance.ActivateRequested += () => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } window)
            { window.Show(); window.WindowState = global::Avalonia.Controls.WindowState.Normal; window.Activate(); }
        });
        instance.Listen();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();
}
