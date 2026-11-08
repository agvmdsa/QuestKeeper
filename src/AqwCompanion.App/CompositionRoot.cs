using System.Net;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Infrastructure.Elevation;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Reputation;
using AqwCompanion.App.Shell;
using AqwCompanion.App.Wiki;
using AqwCompanion.Capture;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Wiki;
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
        services.AddSingleton<IActiveInventory, ActiveInventory>();
        services.AddSingleton(sp => new GameDataViewModel(
            sp.GetRequiredService<CaptureSession>(), OnUiThread, sp.GetRequiredService<ForgetCoordinator>()));
        services.AddSingleton(sp => new ForgetCoordinator(
            () => sp.GetRequiredService<StoredData>().Clear(),
            () =>
            [
                sp.GetRequiredService<HardFarmsViewModel>(),
                sp.GetRequiredService<ReputationViewModel>(),
                sp.GetRequiredService<CaptureSession>(),
            ]));
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
