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
                var home = CustomHomeRenderer.Render("<Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions><TextBlock Text='First'/><WrapPanel Grid.Row='1'><TextBlock Text='Second'/></WrapPanel></Grid>", _ => { });
                Assert.IsInstanceOfType<Grid>(home);
                var homeGrid = (Grid)home;
                Assert.AreEqual(2, homeGrid.RowDefinitions.Count);
                Assert.AreEqual(1, Grid.GetRow(homeGrid.Children[1]));
                Assert.IsInstanceOfType<WrapPanel>(homeGrid.Children[1]);
                var window = new MainWindow(new DesktopSettings { JavaPath = "/test/java" });
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
                Assert.AreEqual(8, tabs.ItemCount);
                var visited = 0;
                void Visit(TabControl navigation, string path)
                {
                    for (var i = 0; i < navigation.ItemCount; i++)
                    {
                        navigation.SelectedIndex = i; Dispatcher.UIThread.RunJobs();
                        Assert.IsNotNull(navigation.SelectedContent); visited++;
                        var next = path + "-" + i;
                        if (navigation.SelectedContent is TabControl nested) Visit(nested, next);
                        else if (Environment.GetEnvironmentVariable("PCL_TEST_SCREENSHOTS") is { } folder)
                        {
                            Directory.CreateDirectory(folder);
                            using var frameImage = window.CaptureRenderedFrame();
                            Assert.IsNotNull(frameImage); frameImage.Save(Path.Combine(folder, "desktop" + next + ".png"));
                        }
                    }
                }
                Visit(tabs, "");
                Assert.IsTrue(visited >= 30, "Feature pages must be connected to navigation.");
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
        if (failure is not null) throw new AssertFailedException("Avalonia window failed to initialize: " + failure, failure);
    }
}
