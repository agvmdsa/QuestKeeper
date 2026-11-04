using AqwCompanion.App;
using AqwCompanion.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Desktop;

public class CompositionRootTests
{
    [Fact]
    public void Container_validates_and_resolves_the_main_view_model()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var main = services.GetRequiredService<MainViewModel>();
        Assert.Equal(AppInfo.ProductName, main.Title);
        Assert.NotNull(main.Wiki);
        Assert.NotNull(main.Diagnostics);
    }

    [Fact]
    public void App_info_exposes_non_empty_product_name_and_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.ProductName));
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }
}
