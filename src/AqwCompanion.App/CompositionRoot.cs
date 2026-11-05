using System.Net;
using AqwCompanion.App.Browser;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.SignIn;
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
        services.AddSingleton<IPageParser<CharacterEquipment>, CharacterEquipmentParser>();
        services.AddSingleton<IResultStore<CharacterEquipment>, LatestResultStore<CharacterEquipment>>();
        services.AddSingleton<IngestionService<CharacterEquipment>>();
        services.AddSingleton<IPageFetcher>(sp => new AccountPageFetcher(
            CreateHttpClient(), AppInfo.Version, sp.GetRequiredService<ILogger<AccountPageFetcher>>()));
        services.AddSingleton<ICharacterEquipmentLoader, CharacterEquipmentLoader>();
        services.AddSingleton<IDelay, TaskDelay>();
        services.AddSingleton<ISignInDialog, SignInDialog>();
        services.AddSingleton<AccountViewModel>();
        services.AddSingleton<WikiViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>One long-lived client: no cookies, no automatic redirects (the fetcher validates each hop).</summary>
    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = AccountPageFetcher.Timeout,
        };
}
