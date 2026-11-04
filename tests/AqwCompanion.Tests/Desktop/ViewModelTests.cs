using System.IO.Compression;
using AqwCompanion.App;
using AqwCompanion.App.ViewModels;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Desktop;

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
    public void Wiki_go_command_navigates_the_host_to_the_address()
    {
        var host = new FakeBrowserHost();
        var vm = new WikiViewModel(host) { AddressInput = "https://aqwwiki.wikidot.com/some-page" };

        vm.GoCommand.Execute(null);

        Assert.Equal("https://aqwwiki.wikidot.com/some-page", host.LastNavigated);
    }

    [Fact]
    public void Wiki_current_url_follows_navigation_completed()
    {
        var host = new FakeBrowserHost();
        var vm = new WikiViewModel(host);

        host.Raise("https://aqwwiki.wikidot.com/final");

        Assert.Equal("https://aqwwiki.wikidot.com/final", vm.CurrentUrl);
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
}
