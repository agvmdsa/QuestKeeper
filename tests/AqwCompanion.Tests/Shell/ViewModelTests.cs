using System.IO.Compression;
using AqwCompanion.App;
using AqwCompanion.App.Account;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Reputation;
using AqwCompanion.App.Shell;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Wiki;
using AqwCompanion.App.Wiki;
using AqwCompanion.Core.Diagnostics;
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
        var vm = new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()), new FailedSnapshotBuffer());

        vm.ExportCommand.Execute(null);

        var zip = Assert.Single(Directory.GetFiles(_paths.ExportDirectory, "diagnostics-*.zip"));
        Assert.StartsWith("Exported to ", vm.Status);
        Assert.Contains(zip, vm.Status);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Null(archive.GetEntry("snapshot.txt"));
    }

    [Fact]
    public void Diagnostics_export_includes_snapshot_only_when_opted_in()
    {
        var failures = new FailedSnapshotBuffer();
        failures.Record(PageSnapshot.Create("https://account.aq.com/AQW/Inventory", "<p>page</p>", "1.0.0", DateTimeOffset.UnixEpoch));
        var vm = new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()), failures);

        Assert.False(vm.IncludeSnapshot);
        vm.IncludeSnapshot = true;
        vm.ExportCommand.Execute(null);

        var zip = Assert.Single(Directory.GetFiles(_paths.ExportDirectory, "diagnostics-*.zip"));
        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry("snapshot.txt"));
    }

    [Fact]
    public void Diagnostics_export_reports_a_non_io_failure_instead_of_throwing()
    {
        var vm = new DiagnosticsViewModel(
            new AppPaths("bad\0root"), new DiagnosticsExporter(new SnapshotRedactor()), new FailedSnapshotBuffer());

        var ex = Record.Exception(() => vm.ExportCommand.Execute(null));

        Assert.Null(ex);
        Assert.StartsWith("Export failed:", vm.Status);
    }

    [Fact]
    public void Diagnostics_export_reports_an_io_failure_when_the_export_directory_is_a_file()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.ExportDirectory, "not a directory");
        var vm = new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()), new FailedSnapshotBuffer());

        var ex = Record.Exception(() => vm.ExportCommand.Execute(null));

        Assert.Null(ex);
        Assert.StartsWith("Export failed:", vm.Status);
    }

    [Fact]
    public void Diagnostics_exposes_the_log_directory()
    {
        var vm = new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()), new FailedSnapshotBuffer());

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
    public async Task Signing_out_resets_the_Hard_Farms_and_Reputation_tabs_but_a_dismissed_sign_out_does_not(bool completes)
    {
        var dialog = new FakeSignInDialog { OnShow = _ => Task.FromResult(SignInResult.Connected("Test Hero")) };
        var account = AccountViewModelBuilder.Create(dialog, persistence: new FakeAccountDataPersistence());
        var store = new HardFarmStore(new InMemoryStoredDataStore());
        store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), DateTimeOffset.UnixEpoch);
        var hardFarms = new HardFarmsViewModel(
            store, new FakeHardFarmSync(), new InventoryCardViewModel(new SidebarViewModel()), new FakeUrlLauncher());
        await hardFarms.RestoreAsync();
        var reputationStore = new ReputationStore(new InMemoryStoredDataStore());
        reputationStore.Save(new StoredReputation(
            new ReputationSnapshot([new FactionStanding(1, "Good", 5, 10, 20, 0)], null), "rep.json", DateTimeOffset.UnixEpoch));
        var reputation = new ReputationViewModel(reputationStore, new FakeReputationFilePicker(), new FakeReputationAutoSource(), TimeProvider.System);
        await reputation.RestoreAsync();
        var main = new MainViewModel(
            account, new WikiViewModel(), new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()), new FailedSnapshotBuffer()), hardFarms,
            reputation);
        await account.SignInCommand.ExecuteAsync(null);
        Assert.Single(main.HardFarms.Rows);
        Assert.Single(main.Reputation.Items);
        dialog.OnShow = _ => Task.FromResult(completes ? SignInResult.SignedOut() : SignInResult.Cancelled());

        account.RequestSignOutCommand.Execute(null);
        await account.ConfirmSignOutCommand.ExecuteAsync(null);

        Assert.Equal(completes, main.HardFarms.Rows.Count == 0);
        Assert.Equal(completes, main.HardFarms.ShowEmptyState);
        Assert.Equal(completes, main.Reputation.Items.Count == 0);
        Assert.Equal(completes, main.Reputation.IsEmpty);
    }

    [Fact]
    public async Task Signing_out_deletes_the_reputation_file_the_game_client_wrote()
    {
        var dialog = new FakeSignInDialog { OnShow = _ => Task.FromResult(SignInResult.Connected("Test Hero")) };
        var account = AccountViewModelBuilder.Create(dialog, persistence: new FakeAccountDataPersistence());
        var source = new ReputationFileSource(_paths);
        Directory.CreateDirectory(_paths.ReputationDirectory);
        File.WriteAllText(source.Path, "{}");
        var reputation = new ReputationViewModel(
            new ReputationStore(new InMemoryStoredDataStore()), new FakeReputationFilePicker(), source, TimeProvider.System);
        var main = new MainViewModel(
            account, new WikiViewModel(), new DiagnosticsViewModel(_paths, new DiagnosticsExporter(new SnapshotRedactor()), new FailedSnapshotBuffer()),
            new HardFarmsViewModel(
                new HardFarmStore(new InMemoryStoredDataStore()), new FakeHardFarmSync(), new InventoryCardViewModel(new SidebarViewModel()), new FakeUrlLauncher()),
            reputation);
        await account.SignInCommand.ExecuteAsync(null);
        Assert.True(File.Exists(source.Path));
        dialog.OnShow = _ => Task.FromResult(SignInResult.SignedOut());

        account.RequestSignOutCommand.Execute(null);
        await account.ConfirmSignOutCommand.ExecuteAsync(null);

        Assert.False(File.Exists(source.Path));
        Assert.NotNull(main.Reputation);
    }
}
