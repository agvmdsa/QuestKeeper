using System.Globalization;
using System.Net;
using AqwCompanion.App.Debug;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.Feed;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Infrastructure.AutoStart;
using AqwCompanion.App.Infrastructure.Elevation;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Phone;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Settings;
using AqwCompanion.App.Skua;
using AqwCompanion.App.Shell;
using AqwCompanion.Capture;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Feed;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Phone.Web;
using AqwCompanion.Core.Wiki;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
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
        // The wiki fetcher applies its own deadline per call, so the client has none.
        services.AddSingleton<IWikiPageFetcher>(sp => new WikiPageFetcher(
            CreateHttpClient(System.Threading.Timeout.InfiniteTimeSpan), AppInfo.Version, sp.GetRequiredService<ILogger<WikiPageFetcher>>()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStoredDataStore>(sp => new LiteDbStoredDataStore(sp.GetRequiredService<AppPaths>().DatabasePath));
        services.AddSingleton(sp => new StoredData(
            sp.GetRequiredService<IStoredDataStore>(),
            StoredKeys.Obsolete,
            (operation, exceptionTypeName) => Log.Error("Stored data {SafeOperation} failed: {ExceptionType}", operation, exceptionTypeName)));
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
            sp.GetRequiredService<IActiveInventory>(),
            sp.GetRequiredService<IUrlLauncher>(),
            OnUiThread));
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
            sp.GetRequiredService<IApplicationExit>(),
            debug: sp.GetRequiredService<DebugModeController>()));
        services.AddSingleton<IActiveInventory, ActiveInventory>();
        services.AddSingleton(sp => new GameDataViewModel(sp.GetRequiredService<CaptureSession>(), OnUiThread));
        services.AddSingleton<IAppSettingsStore>(sp => new JsonAppSettingsStore(sp.GetRequiredService<AppPaths>().SettingsPath));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IAutoStart>(sp => new ScheduledTaskAutoStart(
            sp.GetRequiredService<IProcessRunner>(), Environment.ProcessPath, Environment.UserDomainName + "\\" + Environment.UserName, OperatingSystem.IsWindows));
        services.AddSingleton(sp => new FeedSecretStore(sp.GetRequiredService<AppPaths>().FeedTokenPath));
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<IAppSettingsStore>(), sp.GetRequiredService<ForgetCoordinator>(), sp.GetRequiredService<IAutoStart>(),
            sp.GetRequiredService<FeedSecretStore>(), sp.GetRequiredService<PhoneRegistry>(), CopyToClipboardAsync, () => FindClipboard() is not null));
        services.AddSingleton<IFeedSource>(sp => new SessionFeedSource(
            sp.GetRequiredService<CaptureSession>(), sp.GetRequiredService<GameDataViewModel>(), sp.GetRequiredService<HardFarmsViewModel>()));
        services.AddSingleton<ILocalFeedHost>(sp => new HttpListenerFeedHost(
            new FeedHandler(
                sp.GetRequiredService<FeedSecretStore>().GetOrCreateBytes,
                sp.GetRequiredService<IFeedSource>(),
                $"127.0.0.1:{HttpListenerFeedHost.DefaultPort}",
                AppInfo.Version),
            sp.GetRequiredService<IFeedSource>()));
        services.AddSingleton<LocalFeedController>();
        services.AddSingleton<IPhoneFileStore>(sp => new PhoneStore(sp.GetRequiredService<AppPaths>().PhonePath));
        services.AddSingleton(sp => new PhoneRegistry(sp.GetRequiredService<IPhoneFileStore>(), () => DateTimeOffset.UtcNow));
        services.AddSingleton(sp => new PhoneHub(
            sp.GetRequiredService<IFeedSource>(), new NoticeEngine(), new NoticeLog(50), () => DateTimeOffset.UtcNow,
            ex => Log.Warning("Phone hub failed: {ExceptionType}", ex.GetType().Name),
            skua: sp.GetRequiredService<ISkuaFeed>()));
        services.AddSingleton(sp => new SkuaScriptPrefs(
            new SkuaScriptFile(sp.GetRequiredService<AppPaths>().SkuaScriptPrefsPath), () => DateTimeOffset.UtcNow));
        services.AddSingleton(sp => new SkuaOptionCache(
            new SkuaScriptFile(sp.GetRequiredService<AppPaths>().SkuaScriptOptionsPath), () => DateTimeOffset.UtcNow));
        services.AddSingleton(sp => new SkuaApi(
            sp.GetRequiredService<ISkuaFeed>(), sp.GetRequiredService<ISkuaCommandChannel>(), sp.GetRequiredService<SkuaScriptPrefs>(),
            sp.GetRequiredService<SkuaOptionCache>(), CultureInfo.CurrentCulture));
        services.AddSingleton<ISkuaLogStores>(sp => new SkuaLogStores(sp.GetRequiredService<AppPaths>().Root));
        services.AddSingleton(sp => new SkuaHub(sp.GetRequiredService<ISkuaLogStores>(), () => DateTimeOffset.UtcNow));
        services.AddSingleton<ISkuaFeed>(sp => sp.GetRequiredService<SkuaHub>());
        services.AddSingleton<ISkuaCommandChannel>(sp => sp.GetRequiredService<SkuaHub>());
        services.AddSingleton(sp => new SkuaBridgeHost(
            sp.GetRequiredService<SkuaHub>(), ex => Log.Warning("Skua pipe failed: {ExceptionType}", ex.GetType().Name)));
        services.AddSingleton(sp => new SkuaSettingsPresenter(
            sp.GetRequiredService<ISkuaFeed>(), sp.GetRequiredService<SettingsViewModel>(), OnUiThread));
        services.AddSingleton<IAssetSource, EmbeddedAssetSource>();
        services.AddSingleton<ITailscaleProcess, TailscaleProcess>();
        services.AddSingleton<IServeMarker>(sp => new FileServeMarker(sp.GetRequiredService<AppPaths>().PhoneServeMarkerPath));
        services.AddSingleton<ITailscale>(sp => new TailscaleService(sp.GetRequiredService<ITailscaleProcess>()));
        services.AddSingleton(sp =>
        {
            // The controller needs the host, which needs this handler: resolve the controller on first use and keep it, so a request pays one field read.
            var controller = new Lazy<PhoneController>(() => sp.GetRequiredService<PhoneController>());
            return new PhoneHandler(
                sp.GetRequiredService<PhoneRegistry>(), sp.GetRequiredService<PhoneHub>(), sp.GetRequiredService<IAssetSource>(),
                host => controller.Value.AllowedHost(host), AppInfo.Version, sp.GetRequiredService<SkuaApi>());
        });
        services.AddSingleton<IPhoneHost>(sp => new PhoneHost(
            sp.GetRequiredService<PhoneHandler>(), sp.GetRequiredService<PhoneHub>(), sp.GetRequiredService<PhoneRegistry>()));
        services.AddSingleton<PhoneController>(sp => new PhoneController(
            sp.GetRequiredService<SettingsViewModel>(), sp.GetRequiredService<IPhoneHost>(), sp.GetRequiredService<PhoneHub>(), sp.GetRequiredService<PhoneRegistry>(),
            sp.GetRequiredService<ITailscale>(), sp.GetRequiredService<IServeMarker>(), onUiThread: OnUiThread));
        services.AddSingleton<IPairingLinkSource>(sp => sp.GetRequiredService<PhoneController>());
        services.AddSingleton<ISettingsDialog>(sp => new AvaloniaSettingsDialog(sp.GetRequiredService<IPairingLinkSource>(), sp.GetRequiredService<PhoneRegistry>()));
        services.AddSingleton<IDebugWindowHost, AvaloniaDebugWindowHost>();
        services.AddSingleton<DebugModeController>();
        services.AddSingleton(sp => new ForgetCoordinator(
            () => sp.GetRequiredService<StoredData>().Clear(),
            () => ForgetParticipants(sp)));
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>The participants of "Forget my data", in the order the coordinator runs them.</summary>
    internal static IEnumerable<IForgettable> ForgetParticipants(IServiceProvider sp) =>
    [
        sp.GetRequiredService<HardFarmsViewModel>(),
        new LegacyReputationFolder(sp.GetRequiredService<AppPaths>()),
        sp.GetRequiredService<CaptureSession>(),
        sp.GetRequiredService<SkuaHub>(),
        sp.GetRequiredService<SkuaScriptPrefs>(),
        sp.GetRequiredService<SkuaOptionCache>(),
        // PhoneController MUST stay the LAST participant: its Forget resets the phone hub, which reads the feed, and then brings
        // the phone host back. Every other participant has to have forgotten first, or the host returns serving data from before
        // the Forget. CompositionRootTests.The_phone_controller_is_the_last_forget_participant guards this order.
        sp.GetRequiredService<PhoneController>(),
    ];

    private static IClipboard? FindClipboard() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.Select(w => w.Clipboard).FirstOrDefault(c => c is not null)
            : null;

    // Puts text on the clipboard of the first open window; used by the debug pairing link only.
    private static async Task CopyToClipboardAsync(string text)
    {
        if (FindClipboard() is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
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
