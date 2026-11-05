using AqwCompanion.App;
using AqwCompanion.App.ViewModels;
using AqwCompanion.App.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Desktop;

public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void Main_window_opens_with_product_title_and_account_as_the_first_of_four_tabs()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal(AppInfo.ProductName, window.Title);
        var tabs = window.FindControl<TabControl>("Tabs")!;
        Assert.Equal(4, tabs.ItemCount);
        Assert.Equal("Account", Assert.IsType<TabItem>(tabs.Items[0]).Header);
        Assert.Empty(window.GetVisualDescendants().OfType<NativeWebView>());
    }
}
