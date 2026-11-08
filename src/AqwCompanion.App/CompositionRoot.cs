using System.Net;
using AqwCompanion.App.Account;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Infrastructure.Browser;
using AqwCompanion.App.Infrastructure.Elevation;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Reputation;
using AqwCompanion.App.Shell;
using AqwCompanion.App.SignIn;
using AqwCompanion.App.Wiki;
using AqwCompanion.Capture;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Core.Site;
using Avalonia.Threading;
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
        services.AddSingleton<WebViewBrowserHost>();
        services.AddSingleton<IPageParser<AccountIdentity>, AccountHomeParser>();
        services.AddSingleton<LatestResultStore<AccountIdentity>>();
        services.AddSingleton<IResultStore<AccountIdentity>>(sp => sp.GetRequiredService<LatestResultStore<AccountIdentity>>());
        services.AddSingleton<IngestionService<AccountIdentity>>();
        services.AddSingleton<IPageParser<CharacterEquipment>, CharacterEquipmentParser>();
        services.AddSingleton<LatestResultStore<CharacterEquipment>>();
        services.AddSingleton<IResultStore<CharacterEquipment>>(sp => sp.GetRequiredService<LatestResultStore<CharacterEquipment>>());
        services.AddSingleton<IngestionService<CharacterEquipment>>();
        services.AddSingleton<IPageParser<CharacterBadges>, BadgesParser>();
        services.AddSingleton<LatestResultStore<CharacterBadges>>();
        services.AddSingleton<IResultStore<CharacterBadges>>(sp => sp.GetRequiredService<LatestResultStore<CharacterBadges>>());
        services.AddSingleton<IngestionService<CharacterBadges>>();
        services.AddSingleton<IPageParser<CharacterInventory>, InventoryParser>();
        services.AddSingleton<LatestResultStore<CharacterInventory>>();
        services.AddSingleton<IResultStore<CharacterInventory>>(sp => sp.GetRequiredService<LatestResultStore<CharacterInventory>>());
        services.AddSingleton<IngestionService<CharacterInventory>>();
        services.AddSingleton<IPageFetcher>(sp => new AccountPageFetcher(
            CreateHttpClient(AccountPageFetcher.Timeout), AppInfo.Version, sp.GetRequiredService<ILogger<AccountPageFetcher>>()));
        // The wiki fetcher applies its own deadline per call, so the client has none.
        services.AddSingleton<IWikiPageFetcher>(sp => new WikiPageFetcher(
            CreateHttpClient(System.Threading.Timeout.InfiniteTimeSpan), AppInfo.Version, sp.GetRequiredService<ILogger<WikiPageFetcher>>()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStoredDataStore>(sp => new LiteDbStoredDataStore(sp.GetRequiredService<AppPaths>().DatabasePath));
        services.AddSingleton(sp => new StoredData(
            sp.GetRequiredService<IStoredDataStore>(),
            StoredKeys.Obsolete,
            (operation, exceptionTypeName) => Log.Error("Stored data {SafeOperation} failed: {ExceptionType}", operation, exceptionTypeName)));
        services.AddSingleton<IAccountDataPersistence, AccountDataPersistence>();
        services.AddSingleton<IAccountSessionData, AccountSessionData>();
        services.AddSingleton<ICharacterEquipmentLoader, CharacterEquipmentLoader>();
        services.AddSingleton<ICharacterBadgesLoader, CharacterBadgesLoader>();
        services.AddSingleton<ICharacterInventoryLoader, CharacterInventoryLoader>();
        services.AddSingleton<IDelay, TaskDelay>();
        services.AddSingleton<IHardFarmStore, HardFarmStore>();
        services.AddSingleton<IHardFarmSync>(sp => new HardFarmSync(
            sp.GetRequiredService<IWikiPageFetcher>(),
            sp.GetRequiredService<IHardFarmStore>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IUrlLauncher, ProcessUrlLauncher>();
        services.AddSingleton(sp => new HardFarmsViewModel(
            sp.GetRequiredService<IHardFarmStore>(),
            sp.GetRequiredService<IHardFarmSync>(),
            sp.GetRequiredService<ActiveInventory>(),
            sp.GetRequiredService<IUrlLauncher>(),
            OnUiThread));
        services.AddSingleton<IReputationStore, ReputationStore>();
        services.AddSingleton<IReputationFilePicker, ReputationFilePicker>();
        services.AddSingleton<IReputationAutoSource>(sp => new ReputationFileSource(sp.GetRequiredService<AppPaths>()));
        services.AddSingleton<ReputationViewModel>();
        services.AddSingleton<IGameDataStore, GameDataStore>();
        services.AddSingleton<IProcessElevation, WindowsProcessElevation>();
        services.AddSingleton<IApplicationExit, AvaloniaApplicationExit>();
        services.AddSingleton(sp => new CaptureSession(
            () => OperatingSystem.IsWindows() ? new LivePacketSource() : new NullPacketSource(),
            sp.GetRequiredService<IProcessElevation>(),
            NpcapAvailability.Check,
            OperatingSystem.IsWindows,
            sp.GetRequiredService<IGameDataStore>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IApplicationExit>(),
            sp.GetRequiredService<AppPaths>()));
        services.AddSingleton<AvaloniaMainWindowHost>();
        services.AddSingleton<IMainWindowHost>(sp => sp.GetRequiredService<AvaloniaMainWindowHost>());
        services.AddSingleton(sp => new TrayController(
            sp.GetRequiredService<CaptureSession>(),
            sp.GetRequiredService<IMainWindowHost>(),
            sp.GetRequiredService<IApplicationExit>()));
        services.AddSingleton<ActiveInventory>();
        services.AddSingleton(sp => new GameDataViewModel(sp.GetRequiredService<CaptureSession>(), OnUiThread));
        services.AddSingleton(sp => new ForgetCoordinator(
            () => sp.GetRequiredService<IAccountDataPersistence>().Forget(),
            () =>
            [
                sp.GetRequiredService<AccountViewModel>(),
                sp.GetRequiredService<HardFarmsViewModel>(),
                sp.GetRequiredService<ReputationViewModel>(),
                sp.GetRequiredService<CaptureSession>(),
            ]));
        services.AddSingleton<ISignInDialog, SignInDialog>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<SummaryCardViewModel>();
        services.AddSingleton<EquipmentCardViewModel>();
        services.AddSingleton<BadgesCardViewModel>();
        services.AddSingleton<InventoryCardViewModel>();
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

    // Runs the action at once on the UI thread, otherwise posts it there; for view models fed from background threads.
    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>One long-lived client: no cookies, no automatic redirects (the fetcher validates each hop).</summary>
    private static HttpClient CreateHttpClient(TimeSpan timeout) =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = timeout,
        };
}
