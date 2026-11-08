using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.App.Shell;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Shell;

public class TrayWindowTests
{
    private static MainWindow Create(out ServiceProvider services, out TrayController controller, out FakeApplicationExit exit)
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        services = CompositionRoot.Build(paths);
        var host = services.GetRequiredService<AvaloniaMainWindowHost>();
        exit = new FakeApplicationExit();
        controller = new TrayController(
            services.GetRequiredService<CaptureSession>(), host, exit);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };
        host.Attach(window);
        window.KeepInTray(controller);
        return window;
    }

    [AvaloniaFact]
    public void Closing_the_window_as_the_user_hides_it_and_keeps_it_alive()
    {
        var window = Create(out var services, out var controller, out _);
        using (services)
        {
            window.Show();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();

            Assert.False(window.IsVisible);
            Assert.False(closed);

            controller.Open();
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
    }

    [AvaloniaFact]
    public async Task After_exit_started_closing_the_window_really_closes_it()
    {
        var window = Create(out var services, out var controller, out var exit);
        using (services)
        {
            window.Show();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            await controller.ExitAsync();
            window.Close();

            Assert.True(closed);
            Assert.Equal(1, exit.ExitCalls);
        }
    }

    [AvaloniaFact]
    public void The_tray_icon_can_be_built_under_the_headless_platform()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        using var setup = new TrayIconSetup(services.GetRequiredService<TrayController>());

        Assert.Equal("AQW Companion - not capturing", setup.Icon.ToolTipText);
        Assert.Equal(4, setup.Icon.Menu!.Items.Count);
    }
}
