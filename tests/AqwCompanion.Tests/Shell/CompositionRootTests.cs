using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Infrastructure.Elevation;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Wiki;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqwCompanion.Tests.Shell;

public class CompositionRootTests
{
    [Fact]
    public void Container_validates_and_resolves_the_main_view_model()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var main = services.GetRequiredService<MainViewModel>();
        Assert.Equal(AppInfo.ProductName, main.Title);
        Assert.NotNull(main.Diagnostics);
        Assert.NotNull(main.GameData);
    }

    [Fact]
    public void Container_resolves_the_hard_farm_pipeline_and_shares_the_view_model_with_the_shell()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<WikiPageFetcher>(services.GetRequiredService<IWikiPageFetcher>());
        Assert.IsType<HardFarmSync>(services.GetRequiredService<IHardFarmSync>());
        Assert.IsType<ProcessUrlLauncher>(services.GetRequiredService<IUrlLauncher>());
        Assert.Same(services.GetRequiredService<HardFarmsViewModel>(), services.GetRequiredService<MainViewModel>().HardFarms);
    }

    [Fact]
    public void Container_resolves_the_active_inventory_over_the_game_data()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var inventory = services.GetRequiredService<IActiveInventory>();
        Assert.IsType<ActiveInventory>(inventory);
        Assert.Null(inventory.Current);
    }

    [Fact]
    public void Container_resolves_the_delay_seam()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<TaskDelay>(services.GetRequiredService<IDelay>());
    }

    [Fact]
    public void Container_resolves_the_persistence_services_without_creating_the_database_file()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<LiteDbStoredDataStore>(services.GetRequiredService<IStoredDataStore>());
        Assert.NotNull(services.GetRequiredService<StoredData>());
        Assert.Same(TimeProvider.System, services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Container_resolves_the_capture_session_and_its_parts()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var session = services.GetRequiredService<CaptureSession>();
        Assert.Same(session, services.GetRequiredService<CaptureSession>());
        Assert.Equal(CaptureState.Idle, session.State);
        Assert.IsType<GameDataStore>(services.GetRequiredService<IGameDataStore>());
        Assert.IsType<WindowsProcessElevation>(services.GetRequiredService<IProcessElevation>());
        Assert.IsType<AvaloniaApplicationExit>(services.GetRequiredService<IApplicationExit>());
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void Container_resolves_the_tray_controller_over_the_session_and_the_window_host()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var controller = services.GetRequiredService<TrayController>();
        Assert.Same(controller, services.GetRequiredService<TrayController>());
        Assert.IsType<AvaloniaMainWindowHost>(services.GetRequiredService<IMainWindowHost>());
        Assert.Same(services.GetRequiredService<IMainWindowHost>(), services.GetRequiredService<AvaloniaMainWindowHost>());
        Assert.Equal("AQW Companion - not capturing", controller.ToolTip);
        Assert.False(controller.IsExiting);
    }

    [Fact]
    public async Task The_capture_session_resolved_from_the_container_takes_part_in_forgetting()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        try
        {
            using var services = CompositionRoot.Build(paths);
            var profile = new AqwCompanion.Core.Capture.CharacterProfile("Fictional Hero", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0);
            services.GetRequiredService<IGameDataStore>().Save([new AqwCompanion.Core.Capture.CharacterSnapshot(profile, [], [], [], DateTimeOffset.UnixEpoch)]);
            var session = services.GetRequiredService<CaptureSession>();
            await session.RestoreAsync();
            Assert.Single(session.Characters);

            var result = services.GetRequiredService<ForgetCoordinator>().Forget();

            Assert.True(result.IsComplete);
            Assert.Empty(session.Characters);
            Assert.Empty(services.GetRequiredService<IGameDataStore>().Load());
        }
        finally
        {
            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }
    }

    [Fact]
    public void App_info_exposes_non_empty_product_name_and_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.ProductName));
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }
}
