using AqwCompanion.App;
using AqwCompanion.App.Shell;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Shell;

public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void Main_window_opens_with_product_title_and_hard_farms_as_the_first_of_three_tabs()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal(AppInfo.ProductName, window.Title);
        var tabs = window.FindControl<TabControl>("Tabs")!;
        Assert.Equal(3, tabs.ItemCount);
        Assert.Equal("Settings", window.FindControl<Button>("SettingsButton")!.Content);
        Assert.Equal("Hard Farms", Assert.IsType<TabItem>(tabs.Items[0]).Header);
    }
}
