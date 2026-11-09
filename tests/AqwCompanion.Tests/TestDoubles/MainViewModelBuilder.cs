using AqwCompanion.App;
using AqwCompanion.App.Debug;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Settings;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Forgetting;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A <see cref="MainViewModel"/> over a capture rig and fakes, with nothing stored or opened.</summary>
public static class MainViewModelBuilder
{
    public sealed record Built(
        MainViewModel Main, CaptureRig Rig, FakeActiveInventory Inventory, FakeSettingsDialog Dialog, FakeDebugWindowHost DebugHost,
        FakeAppSettingsStore Settings, FakeHardFarmSync Sync, HardFarmStore Store);

    public static Built Create(CaptureRig? rig = null, Action<Action>? onUiThread = null)
    {
        rig ??= new CaptureRig();
        var inventory = new FakeActiveInventory();
        var dialog = new FakeSettingsDialog();
        var debugHost = new FakeDebugWindowHost();
        var settingsStore = new FakeAppSettingsStore();
        var sync = new FakeHardFarmSync();
        var settings = new SettingsViewModel(settingsStore, new ForgetCoordinator(() => true, () => []));
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        var store = new HardFarmStore(new InMemoryStoredDataStore().Module());
        var main = new MainViewModel(
            new DiagnosticsViewModel(paths, new DiagnosticsExporter(new SnapshotRedactor())),
            new HardFarmsViewModel(store, sync, inventory, new FakeUrlLauncher()),
            rig.CreateViewModel(onUiThread),
            settings,
            dialog,
            new DebugModeController(settings, debugHost));
        return new Built(main, rig, inventory, dialog, debugHost, settingsStore, sync, store);
    }
}
