using AqwCompanion.App;
using AqwCompanion.App.Dashboard;
using AqwCompanion.App.Shell;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Shell;

public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void Main_window_opens_with_the_product_title_the_dashboard_and_no_tabs()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal(AppInfo.ProductName, window.Title);
        Assert.Single(window.GetVisualDescendants().OfType<DashboardView>());
        Assert.Empty(window.GetVisualDescendants().OfType<TabControl>());
        Assert.Equal("Settings", window.FindControl<Button>("SettingsButton")!.Content);
    }

    [AvaloniaFact]
    public void Ctrl_Shift_D_is_bound_to_the_debug_toggle()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        var binding = Assert.Single(window.KeyBindings);

        Assert.Equal(Avalonia.Input.Key.D, binding.Gesture.Key);
        Assert.Equal(Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift, binding.Gesture.KeyModifiers);
    }
}
