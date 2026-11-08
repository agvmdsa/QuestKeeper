using System.IO.Compression;
using AqwCompanion.App;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Reputation;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Wiki;
using AqwCompanion.App.Wiki;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Shell;

public sealed class ViewModelTests : IDisposable
{
    private readonly AppPaths _paths =
        new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public void Wiki_view_model_builds_without_a_browser_host()
    {
        Assert.NotNull(new WikiViewModel());
    }

    [Fact]
    public void Diagnostics_export_writes_a_zip_into_the_export_directory()
    {
        var vm = new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()));

        vm.ExportCommand.Execute(null);

        var zip = Assert.Single(Directory.GetFiles(_paths.ExportDirectory, "diagnostics-*.zip"));
        Assert.StartsWith("Exported to ", vm.Status);
        Assert.Contains(zip, vm.Status);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Null(archive.GetEntry("snapshot.txt"));
    }

    [Fact]
    public void Diagnostics_export_reports_a_non_io_failure_instead_of_throwing()
    {
        var vm = new DiagnosticsViewModel(
            new AppPaths("bad\0root"), new DiagnosticsExporter(new SnapshotRedactor()));

        var ex = Record.Exception(() => vm.ExportCommand.Execute(null));

        Assert.Null(ex);
        Assert.StartsWith("Export failed (", vm.Status);
        Assert.DoesNotContain("Null character", vm.Status);
    }

    [Fact]
    public void Diagnostics_export_reports_an_io_failure_when_the_export_directory_is_a_file()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.ExportDirectory, "not a directory");
        var vm = new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()));

        var ex = Record.Exception(() => vm.ExportCommand.Execute(null));

        Assert.Null(ex);
        Assert.StartsWith("Export failed (", vm.Status);
        Assert.DoesNotContain("not a directory", vm.Status);
    }

    [Fact]
    public void Diagnostics_exposes_the_log_directory()
    {
        var vm = new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()));

        Assert.Equal(_paths.LogDirectory, vm.LogDirectory);
    }

    [Fact]
    public void App_paths_hang_off_the_root()
    {
        Assert.Equal(Path.Combine(_paths.Root, "logs"), _paths.LogDirectory);
        Assert.Equal(Path.Combine(_paths.Root, "crashes"), _paths.CrashDirectory);
        Assert.Equal(Path.Combine(_paths.Root, "exports"), _paths.ExportDirectory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Forgetting_empties_the_Hard_Farms_and_Reputation_tabs_but_a_cancelled_request_does_not(bool confirms)
    {
        HardFarmsViewModel? hardFarmsRef = null;
        ReputationViewModel? reputationRef = null;
        var coordinator = new ForgetCoordinator(() => true, () => [hardFarmsRef!, reputationRef!]);
        var store = new HardFarmStore(new InMemoryStoredDataStore().Module());
        store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), DateTimeOffset.UnixEpoch);
        var hardFarms = new HardFarmsViewModel(
            store, new FakeHardFarmSync(), ActiveInventoryBuilder.Empty(), new FakeUrlLauncher());
        hardFarmsRef = hardFarms;
        await hardFarms.RestoreAsync();
        var reputationStore = new ReputationStore(new InMemoryStoredDataStore().Module());
        reputationStore.Save(new StoredReputation(
            new ReputationSnapshot([new FactionStanding(1, "Good", 5, 10, 20, 0)], null), "rep.json", DateTimeOffset.UnixEpoch));
        var reputation = new ReputationViewModel(reputationStore, new FakeReputationFilePicker(), new FakeReputationAutoSource(), TimeProvider.System);
        reputationRef = reputation;
        await reputation.RestoreAsync();
        var main = new MainViewModel(
            new WikiViewModel(), new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor())), hardFarms,
            reputation, new CaptureRig().CreateViewModel(forgetting: coordinator));
        Assert.Single(main.HardFarms.Rows);
        Assert.Single(main.Reputation.Items);

        main.GameData.RequestForgetCommand.Execute(null);
        (confirms ? main.GameData.ConfirmForgetCommand : main.GameData.CancelForgetCommand).Execute(null);

        Assert.Equal(confirms, main.HardFarms.Rows.Count == 0);
        Assert.Equal(confirms, main.HardFarms.ShowEmptyState);
        Assert.Equal(confirms, main.Reputation.Items.Count == 0);
        Assert.Equal(confirms, main.Reputation.IsEmpty);
    }

    [Fact]
    public void Forgetting_deletes_the_reputation_file_the_game_client_wrote()
    {
        ReputationViewModel? reputationRef = null;
        var coordinator = new ForgetCoordinator(() => true, () => [reputationRef!]);
        var source = new ReputationFileSource(_paths);
        Directory.CreateDirectory(_paths.ReputationDirectory);
        File.WriteAllText(source.Path, "{}");
        var reputation = new ReputationViewModel(
            new ReputationStore(new InMemoryStoredDataStore().Module()), new FakeReputationFilePicker(), source, TimeProvider.System);
        reputationRef = reputation;
        var main = new MainViewModel(
            new WikiViewModel(), new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor())),
            new HardFarmsViewModel(
                new HardFarmStore(new InMemoryStoredDataStore().Module()), new FakeHardFarmSync(), ActiveInventoryBuilder.Empty(), new FakeUrlLauncher()),
            reputation, new CaptureRig().CreateViewModel(forgetting: coordinator));
        Assert.True(File.Exists(source.Path));

        main.GameData.RequestForgetCommand.Execute(null);
        main.GameData.ConfirmForgetCommand.Execute(null);

        Assert.False(File.Exists(source.Path));
    }
}
