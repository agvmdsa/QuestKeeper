using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmUpdaterTests
{
    private static readonly DateTimeOffset Now = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Pacing = TimeSpan.FromMilliseconds(3000);

    private readonly List<string> _log = [];
    private readonly InMemoryStoredDataStore _inner = new();
    private readonly LoggingDelay _delay;
    private readonly FakeWikiFetcher _fetcher;
    private readonly HardFarmStore _store;

    public HardFarmUpdaterTests()
    {
        _delay = new LoggingDelay(_log);
        _fetcher = new FakeWikiFetcher { Log = _log };
        _store = new HardFarmStore(_inner);
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 6)), ("Legion", WikiPages.Slugs("leg", 5)));
        foreach (var slug in WikiUrls.TypeIndexSlugs)
        {
            _fetcher.Pages[slug] = WikiPages.Index(($"{slug}-item", $"Item of {slug}"));
        }

        // The Hard Farming slugs appear in the class and armor indexes (armor lower priority).
        _fetcher.Pages["classes"] = WikiPages.Index(("nat-1", "Nation Class (Class)"), ("classes-item", "Item of classes"));
        _fetcher.Pages["armors"] = WikiPages.Index(("nat-1", "Nation Armor"), ("nat-2", "Nation Armor Two"));
    }

    private HardFarmUpdater Build(TimeSpan? pacing = null) =>
        new(_fetcher, _store, _delay, new FixedClock(Now), pacing);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task A_full_update_requests_robots_then_the_list_then_the_indexes_in_order_with_a_pause_before_each_but_the_first()
    {
        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Updated, result.Status);
        Assert.Equal(["robots.txt", "syfarm", .. WikiUrls.TypeIndexSlugs], _fetcher.Requested);
        Assert.Equal(17, HardFarmUpdater.TotalSteps);
        Assert.Equal(16, _delay.Delays.Count);
        Assert.All(_delay.Delays, d => Assert.True(d >= TimeSpan.FromSeconds(3)));
        Assert.Equal("fetch:robots.txt", _log[0]);
        for (var i = 1; i < _log.Count; i += 2)
        {
            Assert.Equal("delay", _log[i]);
            Assert.StartsWith("fetch:", _log[i + 1]);
        }
    }

    [Fact]
    public async Task The_default_pacing_is_three_seconds_and_it_can_be_injected()
    {
        await Build().UpdateAsync(null, CancellationToken.None);
        Assert.All(_delay.Delays, d => Assert.Equal(TimeSpan.FromSeconds(3), d));

        _delay.Delays.Clear();
        await Build(TimeSpan.FromMilliseconds(5)).UpdateAsync(null, CancellationToken.None);
        Assert.All(_delay.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(5), d));
    }

    [Fact]
    public async Task Progress_counts_steps_with_a_label()
    {
        var seen = new List<HardFarmProgress>();

        await Build().UpdateAsync(seen.Add, CancellationToken.None);

        Assert.Equal(17, seen.Count);
        Assert.Equal("Step 1 of 17: robots.txt", seen[0].Text);
        Assert.Equal(2, seen[1].Step);
        Assert.Equal("Step 3 of 17: /classes", seen[2].Text);
        Assert.Equal("Step 17 of 17: /enhancements", seen[^1].Text);
    }

    [Fact]
    public async Task A_full_update_stores_the_list_and_the_merged_type_table_with_the_read_time()
    {
        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(11, _store.LoadList()!.List.Count);
        Assert.Equal(Now, _store.LoadList()!.ReadAtUtc);
        var table = _store.LoadTypes()!.Table;
        Assert.Equal(new WikiTypeRow("nat-1", "Nation Class (Class)", WikiType.Class), table.Find("nat-1"));
        Assert.Equal(WikiType.Armor, table.Find("nat-2")!.Type);
        Assert.Equal(WikiType.Pet, table.Find("battle-pets-item")!.Type);
        Assert.Equal(11, result.List!.Count);
        Assert.Equal(table.Count, result.Types!.Count);
        Assert.Equal(Now, result.ReadAtUtc);
    }

    [Fact]
    public async Task An_unreadable_robots_file_stops_before_anything_else_is_requested()
    {
        _fetcher.RobotsError = "HTTP 500";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Failed, result.Status);
        Assert.Contains("robots.txt", result.Message);
        Assert.Equal(["robots.txt"], _fetcher.Requested);
        Assert.Empty(_inner.Items);
    }

    [Theory]
    [InlineData("syfarm")]
    [InlineData("pets")]
    public async Task A_disallowed_needed_path_stops_before_any_page_is_requested(string slug)
    {
        _fetcher.Robots = $"User-agent: *\nDisallow: /{slug}\n";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Failed, result.Status);
        Assert.Contains("/" + slug, result.Message);
        Assert.Equal(["robots.txt"], _fetcher.Requested);
        Assert.Empty(_delay.Delays);
        Assert.Empty(_inner.Items);
    }

    [Fact]
    public async Task A_robots_file_that_only_blocks_other_paths_does_not_stop_the_update()
    {
        _fetcher.Robots = "User-agent: *\nDisallow: /admin:*\nDisallow: /search:*\n";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Updated, result.Status);
    }

    [Fact]
    public async Task A_failed_list_request_stops_at_once_and_keeps_the_previous_data()
    {
        var old = new HardFarmList([new HardFarmGroup("Old", ["old-1"])], "old");
        _store.SaveList(old, Now.AddDays(-1));
        _fetcher.Errors["syfarm"] = "HTTP 503";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Failed, result.Status);
        Assert.Contains("HTTP 503", result.Message);
        Assert.Equal(["robots.txt", "syfarm"], _fetcher.Requested);
        Assert.Equal("old", _store.LoadList()!.List.Credit);
        Assert.Equal(Now.AddDays(-1), _store.LoadList()!.ReadAtUtc);
        Assert.Null(result.List);
    }

    [Fact]
    public async Task A_suspicious_list_keeps_the_old_one_reports_it_and_still_reads_the_types()
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("Old", ["old-1"])], "old"), Now.AddDays(-1));
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 3)));

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Partial, result.Status);
        Assert.Contains("previous list was kept", result.Message);
        Assert.Null(result.List);
        Assert.NotNull(result.Types);
        Assert.Equal("old", _store.LoadList()!.List.Credit);
        Assert.NotNull(_store.LoadTypes());
    }

    [Fact]
    public async Task A_list_page_without_entries_fails_and_changes_nothing()
    {
        _fetcher.Pages["syfarm"] = "<html><body><div id=\"page-content\"><p>nothing</p></div></body></html>";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Failed, result.Status);
        Assert.Empty(_inner.Items);
        Assert.Equal(2, _fetcher.Requested.Count);
    }

    [Fact]
    public async Task A_failed_index_request_stops_there_and_a_partial_type_table_is_saved_when_classes_was_read()
    {
        _fetcher.Errors["weapons"] = "timeout";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Partial, result.Status);
        Assert.Equal(["robots.txt", "syfarm", "classes", "armors", "weapons"], _fetcher.Requested);
        Assert.Contains("/weapons", result.Message);
        Assert.Contains("timeout", result.Message);
        Assert.NotNull(_store.LoadList());
        Assert.NotNull(_store.LoadTypes()!.Table.Find("classes-item"));
        Assert.Null(_store.LoadTypes()!.Table.Find("pets-item"));
    }

    [Fact]
    public async Task A_failed_classes_request_keeps_the_previous_type_table()
    {
        var old = new WikiTypeTable([new WikiTypeRow("old", "Old", WikiType.Cape)]);
        _store.SaveTypes(old, Now.AddDays(-1));
        _fetcher.Errors["classes"] = "HTTP 500";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Partial, result.Status);
        Assert.Null(result.Types);
        Assert.Equal(["robots.txt", "syfarm", "classes"], _fetcher.Requested);
        Assert.NotNull(_store.LoadTypes()!.Table.Find("old"));
        Assert.Equal(Now.AddDays(-1), _store.LoadTypes()!.ReadAtUtc);
    }

    [Fact]
    public async Task An_index_that_cannot_be_parsed_is_skipped_and_named_in_the_message()
    {
        _fetcher.Pages["houses"] = "<html><body><div id=\"page-content\"><p>layout changed</p></div></body></html>";
        _fetcher.Pages["grounds"] = "<html><body><div id=\"page-content\"><p>layout changed</p></div></body></html>";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Partial, result.Status);
        Assert.Contains("/houses", result.Message);
        Assert.Contains("/grounds", result.Message);
        Assert.Equal(17, _fetcher.Requested.Count);
        Assert.Null(_store.LoadTypes()!.Table.Find("houses-item"));
        Assert.NotNull(_store.LoadTypes()!.Table.Find("misc-items-item"));
    }

    [Fact]
    public async Task Unreadable_classes_keeps_the_old_types_but_still_stores_the_list()
    {
        _store.SaveTypes(new WikiTypeTable([new WikiTypeRow("old", "Old", WikiType.Cape)]), Now.AddDays(-1));
        _fetcher.Pages["classes"] = "<html><body><div id=\"page-content\"></div></body></html>";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Partial, result.Status);
        Assert.NotNull(result.List);
        Assert.Null(result.Types);
        Assert.NotNull(_store.LoadTypes()!.Table.Find("old"));
    }

    [Fact]
    public async Task Cancelling_during_the_indexes_stops_requests_and_reports_cancelled()
    {
        using var cts = new CancellationTokenSource();
        _fetcher.OnFetch = slug =>
        {
            if (slug == "weapons")
            {
                cts.Cancel();
            }
        };

        var result = await Build().UpdateAsync(null, cts.Token);

        Assert.Equal(HardFarmUpdateStatus.Cancelled, result.Status);
        Assert.Equal(["robots.txt", "syfarm", "classes", "armors", "weapons"], _fetcher.Requested);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelling_before_the_start_does_not_throw_or_store_anything()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Build().UpdateAsync(null, cts.Token);

        Assert.Equal(HardFarmUpdateStatus.Cancelled, result.Status);
        Assert.Empty(_fetcher.Requested);
        Assert.Empty(_inner.Items);
    }

    [Fact]
    public async Task Store_failures_do_not_make_the_update_throw()
    {
        _inner.ThrowOnSave = true;

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Updated, result.Status);
    }

    private void StoreOldList(int count) =>
        _store.SaveList(new HardFarmList([new HardFarmGroup("Old", WikiPages.Slugs("old", count))], "old"), Now.AddDays(-1));

    [Fact]
    public async Task A_new_list_less_than_half_the_stored_one_is_rejected_like_the_ingestion_drop_guard()
    {
        StoreOldList(138);
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 60)));

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Partial, result.Status);
        Assert.Contains("previous list was kept", result.Message);
        Assert.Contains("138", result.Message);
        Assert.Null(result.List);
        Assert.Equal(138, _store.LoadList()!.List.Count);
        Assert.Equal(Now.AddDays(-1), _store.LoadList()!.ReadAtUtc);
        Assert.NotNull(result.Types);
        Assert.Equal(17, _fetcher.Requested.Count);
    }

    [Fact]
    public async Task A_new_list_of_at_least_half_the_stored_one_is_accepted()
    {
        StoreOldList(138);
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 100)));

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Updated, result.Status);
        Assert.Equal(100, result.List!.Count);
        Assert.Equal(100, _store.LoadList()!.List.Count);
    }

    [Theory]
    [InlineData(22, 10, false)]
    [InlineData(20, 10, true)]
    [InlineData(21, 11, true)]
    public async Task The_drop_guard_compares_with_half_of_the_stored_count(int stored, int incoming, bool accepted)
    {
        StoreOldList(stored);
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", incoming)));

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(accepted, result.List is not null);
        Assert.Equal(accepted ? incoming : stored, _store.LoadList()!.List.Count);
    }

    [Fact]
    public async Task A_small_stored_list_accepts_any_new_list()
    {
        StoreOldList(5);
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 10)));

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(HardFarmUpdateStatus.Updated, result.Status);
        Assert.Equal(10, _store.LoadList()!.List.Count);
    }

    [Fact]
    public async Task Classes_that_cannot_be_understood_stops_the_update_at_once()
    {
        _store.SaveTypes(new WikiTypeTable([new WikiTypeRow("old", "Old", WikiType.Cape)]), Now.AddDays(-1));
        _fetcher.Pages["classes"] = "<html><body><div id=\"page-content\"><p>layout changed</p></div></body></html>";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(["robots.txt", "syfarm", "classes"], _fetcher.Requested);
        Assert.Contains("The class index could not be understood", result.Message);
        Assert.Contains("the update was stopped", result.Message);
        Assert.Null(result.Types);
        Assert.NotNull(_store.LoadTypes()!.Table.Find("old"));
        Assert.Equal(Now.AddDays(-1), _store.LoadTypes()!.ReadAtUtc);
    }

    [Fact]
    public async Task A_later_index_that_cannot_be_understood_does_not_stop_the_update()
    {
        _fetcher.Pages["armors"] = "<html><body><div id=\"page-content\"><p>layout changed</p></div></body></html>";

        var result = await Build().UpdateAsync(null, CancellationToken.None);

        Assert.Equal(17, _fetcher.Requested.Count);
        Assert.Contains("/armors", result.Message);
        Assert.NotNull(result.Types);
    }

    [Fact]
    public async Task A_cancelled_update_never_writes_the_type_table()
    {
        using var cts = new CancellationTokenSource();
        _fetcher.OnFetch = slug =>
        {
            if (slug == "weapons")
            {
                cts.Cancel();
            }
        };

        var result = await Build().UpdateAsync(null, cts.Token);

        Assert.Equal(HardFarmUpdateStatus.Cancelled, result.Status);
        Assert.Null(result.Types);
        Assert.Null(_store.LoadTypes());
    }
}

