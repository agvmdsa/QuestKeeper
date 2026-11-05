using AqwCompanion.App;
using AqwCompanion.App.Browser;
using AqwCompanion.App.SignIn;
using AqwCompanion.App.ViewModels;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        Assert.NotNull(main.Account);
    }

    [Fact]
    public void Container_resolves_the_account_ingestion_pipeline_without_creating_a_browser_control()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.NotNull(services.GetRequiredService<IngestionService<AccountIdentity>>());
        Assert.NotNull(services.GetRequiredService<ILogger<IngestionService<AccountIdentity>>>());
        Assert.False(services.GetRequiredService<WebViewBrowserHost>().IsAttached);
    }

    [Fact]
    public void Container_resolves_the_sign_in_dialog_without_opening_a_window_or_a_browser()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<SignInDialog>(services.GetRequiredService<ISignInDialog>());
        Assert.IsType<TaskDelay>(services.GetRequiredService<IDelay>());
        Assert.False(services.GetRequiredService<WebViewBrowserHost>().IsAttached);
    }

    [Fact]
    public void App_info_exposes_non_empty_product_name_and_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.ProductName));
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }
}
