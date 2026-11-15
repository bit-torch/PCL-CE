using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Avalonia;

namespace PCL.CrossPlatform.Test;

[TestClass]
public class DesktopTests
{
    [TestMethod]
    public void AvaloniaWindowLoadsAllPagesWithoutWpf()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
                var window = new MainWindow(new DesktopSettings { JavaPath = "/test/java" });
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                Assert.AreEqual(5, tabs.ItemCount);
                for (var i = 0; i < tabs.ItemCount; i++)
                {
                    tabs.SelectedIndex = i;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsNotNull(tabs.SelectedContent);
                    if (Environment.GetEnvironmentVariable("PCL_TEST_SCREENSHOTS") is { } folder)
                    {
                        Directory.CreateDirectory(folder);
                        using var frameImage = window.CaptureRenderedFrame();
                        Assert.IsNotNull(frameImage);
                        frameImage.Save(Path.Combine(folder, $"desktop-{i}.png"));
                    }
                }
                var dispatcher = new AvaloniaUiDispatcher();
                Assert.IsTrue(dispatcher.CheckAccess());
                var frame = dispatcher.CreateModalFrame();
                dispatcher.Post(frame.Exit); frame.Run();
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.IsBackground = true;
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Avalonia initialization timed out.");
        if (failure is not null) throw new AssertFailedException("Avalonia window failed to initialize.", failure);
    }
}
