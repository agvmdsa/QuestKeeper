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
using AqwCompanion.App.Shell;
using AqwCompanion.Capture;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Feed;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
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
            sp.GetRequiredService<FeedSecretStore>(), sp.GetRequiredService<PhoneRegistry>(), CopyToClipboardAsync));
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
            ex => Log.Warning("Phone hub failed: {ExceptionType}", ex.GetType().Name)));
        services.AddSingleton<IAssetSource, EmptyAssetSource>();
        services.AddSingleton(sp => new PhoneHandler(
            sp.GetRequiredService<PhoneRegistry>(), sp.GetRequiredService<PhoneHub>(), sp.GetRequiredService<IAssetSource>(),
            host => host == "127.0.0.1:47822" || host == "localhost:47822", AppInfo.Version));
        services.AddSingleton<IPhoneHost>(sp => new PhoneHost(
            sp.GetRequiredService<PhoneHandler>(), sp.GetRequiredService<PhoneHub>(), sp.GetRequiredService<PhoneRegistry>()));
        services.AddSingleton<PhoneController>(sp => new PhoneController(
            sp.GetRequiredService<SettingsViewModel>(), sp.GetRequiredService<IPhoneHost>(), sp.GetRequiredService<PhoneHub>(), sp.GetRequiredService<PhoneRegistry>()));
        services.AddSingleton<ISettingsDialog, AvaloniaSettingsDialog>();
        services.AddSingleton<IDebugWindowHost, AvaloniaDebugWindowHost>();
        services.AddSingleton<DebugModeController>();
        services.AddSingleton(sp => new ForgetCoordinator(
            () => sp.GetRequiredService<StoredData>().Clear(),
            () =>
            [
                sp.GetRequiredService<HardFarmsViewModel>(),
                new LegacyReputationFolder(sp.GetRequiredService<AppPaths>()),
                sp.GetRequiredService<CaptureSession>(),
                sp.GetRequiredService<PhoneController>(),
            ]));
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    // Puts text on the clipboard of the first open window; used by the debug pairing link only.
    private static async Task CopyToClipboardAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.Windows.Select(w => w.Clipboard).FirstOrDefault(c => c is not null) is { } clipboard)
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
