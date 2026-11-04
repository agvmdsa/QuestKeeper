using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;
using Microsoft.Extensions.Logging;

namespace AqwCompanion.Tests.Ingestion;

public class IngestionServiceTests
{
    private static readonly ParseDiagnostics Diag = new("fake", "1", "s", [], 3);

    private static PageSnapshot Snapshot(string payload = "<html/>") =>
        PageSnapshot.Create("https://account.aq.com/AQW/Inventory?x=1", payload, "1.0.0", DateTimeOffset.UnixEpoch);

    private static (IngestionService<List<string>> Service, InMemoryStore<List<string>> Store,
        FailedSnapshotBuffer Failures, ListLogger<IngestionService<List<string>>> Log)
        Build(Func<PageSnapshot, ParseResult<List<string>>> parse)
    {
        var store = new InMemoryStore<List<string>>();
        var failures = new FailedSnapshotBuffer();
        var log = new ListLogger<IngestionService<List<string>>>();
        return (new IngestionService<List<string>>(new FakeParser<List<string>>(parse), store, failures, log),
            store, failures, log);
    }

    [Fact]
    public async Task Ok_result_is_persisted_and_logged_once_at_information()
    {
        var (service, store, failures, log) = Build(_ => ParseResult<List<string>>.Ok(["a", "b", "c"], Diag));

        var result = await service.IngestAsync(Snapshot());

        Assert.True(result.Persisted);
        Assert.Single(store.Saved);
        Assert.Equal(3, store.Saved[0].Count);
        Assert.Null(failures.Latest);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(ParseOutcome.Ok, entry.Properties["Outcome"]);
        Assert.Equal("https://account.aq.com/AQW/Inventory", entry.Properties["PageUrl"]);
        Assert.Equal("fake", entry.Properties["Parser"]);
        Assert.Equal(3, entry.Properties["ItemCount"]);
        Assert.Equal(true, entry.Properties["Persisted"]);
        Assert.True(entry.Properties.ContainsKey("DurationMs"));
    }

    [Fact]
    public async Task Suspicious_result_is_not_persisted_and_snapshot_is_buffered()
    {
        var (service, store, failures, log) =
            Build(_ => ParseResult<List<string>>.Suspicious([], "table present but no rows", Diag));
        var snapshot = Snapshot();

        var result = await service.IngestAsync(snapshot);

        Assert.False(result.Persisted);
        Assert.Empty(store.Saved);
        Assert.Same(snapshot, failures.Latest);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("table present but no rows", entry.Properties["Reason"]);
    }

    [Fact]
    public async Task Failed_result_is_not_persisted_and_logged_at_error()
    {
        var (service, store, failures, log) =
            Build(_ => ParseResult<List<string>>.Failed("marker missing", Diag));

        var result = await service.IngestAsync(Snapshot());

        Assert.False(result.Persisted);
        Assert.Empty(store.Saved);
        Assert.NotNull(failures.Latest);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task Large_count_drop_downgrades_ok_to_suspicious_and_keeps_old_data()
    {
        var (service, store, failures, _) = Build(_ => ParseResult<List<string>>.Ok(["a"], Diag with { ItemCount = 20 }));
        store.LastCount = 100;

        var result = await service.IngestAsync(Snapshot());

        Assert.Equal(ParseOutcome.Suspicious, result.Parse.Outcome);
        Assert.Contains("100", result.Parse.Reason);
        Assert.Contains("20", result.Parse.Reason);
        Assert.False(result.Persisted);
        Assert.Empty(store.Saved);
        Assert.NotNull(failures.Latest);
    }

    [Fact]
    public async Task Count_drop_is_ignored_when_previous_count_is_small()
    {
        var (service, store, _, _) = Build(_ => ParseResult<List<string>>.Ok(["a"], Diag with { ItemCount = 1 }));
        store.LastCount = 5;

        var result = await service.IngestAsync(Snapshot());

        Assert.True(result.Persisted);
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task Parser_exception_becomes_failed_result_logged_with_exception()
    {
        var (service, store, failures, log) =
            Build(_ => throw new InvalidOperationException("boom"));

        var result = await service.IngestAsync(Snapshot());

        Assert.Equal(ParseOutcome.Failed, result.Parse.Outcome);
        Assert.Contains("boom", result.Parse.Reason);
        Assert.Empty(store.Saved);
        Assert.NotNull(failures.Latest);
        var entry = Assert.Single(log.Entries);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task Fixture_driven_pipeline_persists_sample_page()
    {
        var store = new InMemoryStore<IReadOnlyList<string>>();
        var service = new IngestionService<IReadOnlyList<string>>(
            new TableRowsTestParser(), store, new FailedSnapshotBuffer(),
            new ListLogger<IngestionService<IReadOnlyList<string>>>());
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventory-sample.html"));

        var result = await service.IngestAsync(
            PageSnapshot.Create("https://account.aq.com/AQW/Inventory", html, "1.0.0", DateTimeOffset.UnixEpoch));

        Assert.True(result.Persisted);
        Assert.Equal(["Sword of Testing", "Helm of Fixtures"], store.Saved[0].Data);
    }

    [Fact]
    public async Task Fixture_driven_pipeline_flags_drifted_layout_without_overwriting()
    {
        var store = new InMemoryStore<IReadOnlyList<string>>();
        var failures = new FailedSnapshotBuffer();
        var service = new IngestionService<IReadOnlyList<string>>(
            new TableRowsTestParser(), store, failures,
            new ListLogger<IngestionService<IReadOnlyList<string>>>());
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventory-drifted.html"));

        var result = await service.IngestAsync(
            PageSnapshot.Create("https://account.aq.com/AQW/Inventory", html, "1.0.0", DateTimeOffset.UnixEpoch));

        Assert.Equal(ParseOutcome.Failed, result.Parse.Outcome);
        Assert.Contains("#inventoryTable", result.Parse.Diagnostics.MissingMarkers);
        Assert.Empty(store.Saved);
        Assert.NotNull(failures.Latest);
    }
}
