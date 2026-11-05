using AqwCompanion.App.Browser;
using AqwCompanion.App.ViewModels;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AqwCompanion.App;

public static class CompositionRoot
{
    public static ServiceProvider Build(AppPaths paths)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        services.AddSingleton(paths);
        services.AddSingleton(_ => new SnapshotRedactor());
        services.AddSingleton<DiagnosticsExporter>();
        services.AddSingleton<FailedSnapshotBuffer>();
        services.AddSingleton<IBrowserHost, NullBrowserHost>();
        services.AddSingleton<WebViewBrowserHost>();
        services.AddSingleton<IPageParser<AccountIdentity>, AccountHomeParser>();
        services.AddSingleton<IResultStore<AccountIdentity>, LatestResultStore<AccountIdentity>>();
        services.AddSingleton<IngestionService<AccountIdentity>>();
        services.AddSingleton(sp => new AccountViewModel(
            sp.GetRequiredService<WebViewBrowserHost>(),
            sp.GetRequiredService<IngestionService<AccountIdentity>>()));
        services.AddSingleton<WikiViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
