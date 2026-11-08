using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmSyncTests
{
    private static readonly DateTimeOffset Start = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);
    private static readonly string[] AllIndexes = [.. WikiUrls.TypeIndexSlugs];

    private readonly List<string> _log = [];
    private readonly InMemoryStoredDataStore _inner = new();
    private readonly ManualClock _clock = new(Start);
    private readonly LoggingDelay _delay;
    private readonly FakeWikiFetcher _fetcher;
    private readonly HardFarmStore _store;

    public HardFarmSyncTests()
    {
        _delay = new LoggingDelay(_log);
        _fetcher = new FakeWikiFetcher { Log = _log };
        _store = new HardFarmStore(_inner.Module());
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 6)), ("Legion", WikiPages.Slugs("leg", 5)));
        foreach (var slug in WikiUrls.TypeIndexSlugs)
        {
            _fetcher.Pages[slug] = WikiPages.Index(($"{slug}-item", $"Item of {slug}"));
        }

        _fetcher.Pages["classes"] = WikiPages.Index(("nat-1", "Nation Class (Class)"), ("classes-item", "Item of classes"));
        _fetcher.Pages["armors"] = WikiPages.Index(("nat-1", "Nation Armor"), ("nat-2", "Nation Armor Two"));
    }

    private HardFarmSync Build(HardFarmSyncOptions? options = null) => new(_fetcher, _store, _delay, _clock, options);

    private Task<SyncResult> Run(HardFarmSync sync, List<SyncProgress>? progress = null, List<SyncUpdate>? updates = null, CancellationToken ct = default) =>
        sync.RunAsync(progress is null ? null : progress.Add, updates is null ? null : updates.Add, ct);

    private static WikiIndex Index(string slug, params (string Slug, string Name)[] entries) =>
        new(slug, WikiTypes.ForIndex(slug), entries.Select(e => new WikiIndexEntry(e.Slug, e.Name)).ToList());

    private IEnumerable<string> PageRequests => _fetcher.Requested.Where(r => r != "robots.txt");

    // --- The happy pass -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_full_pass_requests_robots_then_the_list_then_the_indexes_in_order_with_a_pause_before_each_but_the_first()
    {
        var result = await Run(Build());

        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Empty(result.Pending);
        Assert.Equal(["robots.txt", "syfarm", .. AllIndexes], _fetcher.Requested);
        Assert.Equal(16, _delay.Delays.Count);
        Assert.All(_delay.Delays, d => Assert.Equal(TimeSpan.FromSeconds(3), d));
        Assert.Equal("fetch:robots.txt", _log[0]);
        for (var i = 1; i < _log.Count; i += 2)
        {
            Assert.Equal("delay", _log[i]);
            Assert.StartsWith("fetch:", _log[i + 1]);
        }
    }

    [Fact]
    public async Task The_list_and_every_index_are_stored_and_the_type_table_is_rebuilt_from_them()
    {
        await Run(Build());

        Assert.Equal(11, _store.LoadList()!.List.Count);
        Assert.Equal(Start, _store.LoadList()!.ReadAtUtc);
        Assert.Equal(15, _store.LoadIndexes().Count);
        var types = _store.LoadTypes()!.Table;
        Assert.Equal(WikiType.Class, types.Find("nat-1")!.Type);
        Assert.Equal("Nation Class (Class)", types.Find("nat-1")!.Name);
        Assert.Equal(WikiType.Armor, types.Find("nat-2")!.Type);
    }

    [Fact]
    public async Task The_list_asks_for_30_seconds_and_each_index_for_60()
    {
        await Run(Build());

        Assert.Equal(TimeSpan.FromSeconds(30), _fetcher.Timeouts.First(t => t.Slug == "syfarm").Timeout);
        Assert.All(_fetcher.Timeouts.Where(t => t.Slug != "syfarm"), t => Assert.Equal(TimeSpan.FromSeconds(60), t.Timeout));
    }

    [Fact]
    public async Task Progress_names_the_page_and_counts_the_indexes()
    {
        var progress = new List<SyncProgress>();

        await Run(Build(), progress);

        Assert.Contains(progress, p => p.Label == "robots.txt");
        Assert.Contains(progress, p => p.Label == "Hard Farming list");
        var pets = progress.First(p => p.Label.EndsWith("/pets"));
        Assert.Equal("Index 6 of 15 · /pets", pets.Label);
        Assert.Equal(15, pets.Total);
        Assert.Equal(5, pets.Done);
        Assert.Null(pets.Waiting);
        Assert.Equal(14, progress.Max(p => p.Done));
    }

    // --- Skipping what is recent ----------------------------------------------------------------------------------

    [Fact]
    public async Task An_index_read_within_seven_days_is_skipped_but_an_older_one_is_read_again()
    {
        _store.SaveIndex(Index("armors", ("old-armor", "Old Armor")), Start.AddDays(-6.9));
        _store.SaveIndex(Index("weapons", ("old-weapon", "Old Weapon")), Start.AddDays(-7.1));

        var result = await Run(Build());

        Assert.DoesNotContain("armors", PageRequests);
        Assert.Contains("weapons", PageRequests);
        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Equal(14, PageRequests.Count() - 1);
        Assert.Equal("Old Armor", _store.LoadTypes()!.Table.Find("old-armor")!.Name);
    }

    [Fact]
    public async Task A_pass_over_fresh_indexes_reads_only_robots_and_the_list_and_pauses_once()
    {
        foreach (var slug in AllIndexes)
        {
            _store.SaveIndex(Index(slug, ($"{slug}-x", "X")), Start.AddDays(-1));
        }

        var result = await Run(Build());

        Assert.Equal(["robots.txt", "syfarm"], _fetcher.Requested);
        Assert.Single(_delay.Delays);
        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Empty(result.Pending);
    }

    [Fact]
    public async Task Continue_after_a_stopped_pass_does_only_what_is_missing()
    {
        _fetcher.Script("pets", FakeWikiFetcher.Http(429));
        var first = await Run(Build());
        Assert.Equal(SyncStatus.Partial, first.Status);
        _log.Clear();

        var second = await Run(Build());

        Assert.Equal(["robots.txt", "syfarm", "pets", "battle-pets", "houses", "floor-items", "wall-items", "misc-items", "use-items", "necklaces", "grounds", "enhancements"], _fetcher.Requested);
        Assert.Equal(SyncStatus.Updated, second.Status);
    }

    [Fact]
    public void Pending_is_every_index_when_nothing_was_read_and_only_the_stale_ones_later()
    {
        var sync = Build();
        Assert.Equal(AllIndexes, sync.GetPending());

        foreach (var slug in AllIndexes.Skip(2))
        {
            _store.SaveIndex(Index(slug), Start.AddDays(-2));
        }

        Assert.Equal(["classes", "armors"], sync.GetPending());
    }

    // --- Pacing ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task After_a_slow_answer_the_next_pause_is_doubled_and_only_that_one()
    {
        _fetcher.OnFetch = slug =>
        {
            if (slug == "classes")
            {
                _clock.Advance(TimeSpan.FromSeconds(8.5));
            }
        };

        await Run(Build());

        // delays: before syfarm, before classes, then armors comes after the slow classes answer.
        Assert.Equal(TimeSpan.FromSeconds(3), _delay.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(3), _delay.Delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(6), _delay.Delays[2]);
        Assert.All(_delay.Delays.Skip(3), d => Assert.Equal(TimeSpan.FromSeconds(3), d));
    }

    [Fact]
    public async Task An_answer_of_exactly_eight_seconds_is_not_slow()
    {
        _fetcher.OnFetch = slug =>
        {
            if (slug == "classes")
            {
                _clock.Advance(TimeSpan.FromSeconds(8));
            }
        };

        await Run(Build());

        Assert.All(_delay.Delays, d => Assert.Equal(TimeSpan.FromSeconds(3), d));
    }

    [Fact]
    public async Task The_doubled_pause_is_capped_at_twelve_seconds()
    {
        _fetcher.OnFetch = slug =>
        {
            if (slug == "classes")
            {
                _clock.Advance(TimeSpan.FromSeconds(20));
            }
        };

        await Run(Build(new HardFarmSyncOptions { Pause = TimeSpan.FromSeconds(10) }));

        Assert.Equal(TimeSpan.FromSeconds(10), _delay.Delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(12), _delay.Delays[2]);
        Assert.Equal(TimeSpan.FromSeconds(10), _delay.Delays[3]);
    }

    [Fact]
    public async Task A_slow_robots_answer_slows_the_first_pause_too()
    {
        var slow = new SlowRobots(_fetcher, _clock);

        await new HardFarmSync(slow, _store, _delay, _clock).RunAsync(null, null, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(6), _delay.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(3), _delay.Delays[1]);
    }

    private sealed class SlowRobots(FakeWikiFetcher inner, ManualClock clock) : IWikiPageFetcher
    {
        public Task<PageFetchResult> FetchPageAsync(string url, TimeSpan? timeout = null, CancellationToken ct = default) =>
            inner.FetchPageAsync(url, timeout, ct);

        public Task<PageFetchResult> FetchRobotsAsync(CancellationToken ct = default)
        {
            clock.Advance(TimeSpan.FromSeconds(9));
            return inner.FetchRobotsAsync(ct);
        }
    }

    // --- Retry, backoff and giving up -----------------------------------------------------------------------------

    [Fact]
    public async Task A_timeout_does_not_stop_the_pass_the_page_goes_to_the_back_and_is_tried_again_after_a_wait()
    {
        _fetcher.Script("pets", FakeWikiFetcher.Timeout());
        var progress = new List<SyncProgress>();

        var result = await Run(Build(), progress);

        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Equal(["syfarm", .. AllIndexes, "pets"], PageRequests);
        Assert.Equal(TimeSpan.FromSeconds(10), _delay.Delays[^1]);
        Assert.Contains(progress, p => p.Waiting == "Waiting 10 s before trying /pets again");
        Assert.Equal(15, _store.LoadIndexes().Count);
    }

    [Fact]
    public async Task A_page_that_keeps_failing_is_tried_three_times_with_growing_waits_then_given_up_while_the_pass_goes_on()
    {
        _fetcher.Script("pets", FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout());
        var progress = new List<SyncProgress>();

        var result = await Run(Build(), progress);

        Assert.Equal(3, PageRequests.Count(r => r == "pets"));
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)], _delay.Delays.TakeLast(2));
        Assert.Contains(progress, p => p.Waiting == "Waiting 30 s before trying /pets again");
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Equal(["pets"], result.Pending);
        Assert.Equal(14, _store.LoadIndexes().Count);
        Assert.Contains("/pets", result.Message);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task A_server_error_is_retried_like_a_timeout(int status)
    {
        _fetcher.Script("houses", FakeWikiFetcher.Http(status));

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Equal(2, PageRequests.Count(r => r == "houses"));
    }

    [Fact]
    public async Task A_connection_failure_is_retried_like_a_timeout()
    {
        _fetcher.Script("houses", PageFetchResult.Failure("The request failed.", PageFailureKind.Network));

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Equal(2, PageRequests.Count(r => r == "houses"));
    }

    [Fact]
    public async Task A_missing_page_is_skipped_without_a_retry_and_the_pass_continues()
    {
        _fetcher.Script("houses", FakeWikiFetcher.Http(404));

        var result = await Run(Build());

        Assert.Equal(1, PageRequests.Count(r => r == "houses"));
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Equal(["houses"], result.Pending);
        Assert.Contains("enhancements", PageRequests);
    }

    [Theory]
    [InlineData(PageFailureKind.TooLarge)]
    [InlineData(PageFailureKind.NotHtml)]
    public async Task A_page_that_is_too_large_or_not_html_is_skipped_without_a_retry(PageFailureKind kind)
    {
        _fetcher.Script("houses", PageFetchResult.Failure("odd", kind));

        var result = await Run(Build());

        Assert.Equal(1, PageRequests.Count(r => r == "houses"));
        Assert.Equal(["houses"], result.Pending);
        Assert.Contains("enhancements", PageRequests);
    }

    [Fact]
    public async Task A_plain_legacy_failure_counts_as_a_network_failure_and_is_retried()
    {
        _fetcher.Script("houses", PageFetchResult.Failure("plain"));

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Equal(2, PageRequests.Count(r => r == "houses"));
    }

    [Fact]
    public async Task The_list_page_is_retried_too_and_a_list_that_never_answers_still_lets_the_indexes_be_read()
    {
        _fetcher.Script("syfarm", FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout());

        var result = await Run(Build());

        // The list is alone in the queue with the indexes behind it; after its third failure the pass moves on.
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("Hard Farming list", result.Message);
        Assert.Equal(15, _store.LoadIndexes().Count);
        Assert.Null(_store.LoadList());
    }

    // --- Failures that are not retried still count toward stopping ------------------------------------------------

    [Fact]
    public async Task Three_missing_pages_in_a_row_stop_the_pass_and_keep_what_was_saved()
    {
        _fetcher.Script("classes", FakeWikiFetcher.Http(404));
        _fetcher.Script("armors", FakeWikiFetcher.Http(404));
        _fetcher.Script("weapons", FakeWikiFetcher.Http(404));

        var result = await Run(Build());

        Assert.Equal(["syfarm", "classes", "armors", "weapons"], PageRequests);
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("The Wiki is not answering well; try again later.", result.Message);
        Assert.NotNull(_store.LoadList());
        Assert.Empty(_store.LoadIndexes());
    }

    [Fact]
    public async Task Two_missing_pages_followed_by_a_success_do_not_stop_the_pass()
    {
        _fetcher.Script("classes", FakeWikiFetcher.Http(404));
        _fetcher.Script("armors", FakeWikiFetcher.Http(404));

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.DoesNotContain("not answering well", result.Message);
        Assert.Equal(["classes", "armors"], result.Pending);
        Assert.Equal(13, _store.LoadIndexes().Count);
        Assert.Contains("pets", PageRequests);
    }

    [Fact]
    public async Task Timeouts_and_missing_pages_count_together_toward_stopping()
    {
        _fetcher.Script("classes", FakeWikiFetcher.Timeout());
        _fetcher.Script("armors", FakeWikiFetcher.Http(404));
        _fetcher.Script("weapons", FakeWikiFetcher.Http(404));

        var result = await Run(Build());

        Assert.Equal(["syfarm", "classes", "armors", "weapons"], PageRequests);
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("The Wiki is not answering well; try again later.", result.Message);
    }

    // --- Stopping the pass ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task Forbidden_or_too_many_requests_stop_the_whole_pass_and_keep_what_was_saved(int status)
    {
        _fetcher.Script("weapons", FakeWikiFetcher.Http(status));

        var result = await Run(Build());

        Assert.Equal(["syfarm", "classes", "armors", "weapons"], PageRequests);
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("The Wiki is not answering well; try again later", result.Message);
        Assert.Equal(2, _store.LoadIndexes().Count);
        Assert.NotNull(_store.LoadList());
        Assert.Equal(AllIndexes.Skip(2), result.Pending);
    }

    [Fact]
    public async Task A_blocked_failure_stops_the_whole_pass()
    {
        _fetcher.Script("armors", PageFetchResult.Failure("no", PageFailureKind.Blocked));

        var result = await Run(Build());

        Assert.Equal(["syfarm", "classes", "armors"], PageRequests);
        Assert.Equal(SyncStatus.Partial, result.Status);
    }

    [Fact]
    public async Task A_stopped_pass_that_saved_nothing_is_failed()
    {
        _fetcher.Script("syfarm", FakeWikiFetcher.Http(429));

        var result = await Run(Build());

        Assert.Equal(["syfarm"], PageRequests);
        Assert.Equal(SyncStatus.Failed, result.Status);
        Assert.Equal(AllIndexes, result.Pending);
        Assert.Empty(_store.LoadIndexes());
    }

    [Fact]
    public async Task Three_failed_requests_in_a_row_stop_the_pass_whichever_pages_they_were()
    {
        _fetcher.Script("armors", FakeWikiFetcher.Timeout());
        _fetcher.Script("weapons", FakeWikiFetcher.Http(503));
        _fetcher.Script("helmets-hoods", FakeWikiFetcher.Timeout());

        var result = await Run(Build());

        Assert.Equal(["syfarm", "classes", "armors", "weapons", "helmets-hoods"], PageRequests);
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("The Wiki is not answering well; try again later", result.Message);
        Assert.Equal(AllIndexes.Skip(1), result.Pending);
    }

    [Fact]
    public async Task A_success_in_between_resets_the_count_of_failures()
    {
        // pets and battle-pets fail, houses succeeds, floor-items fails: never three failures in a row.
        _fetcher.Script("pets", FakeWikiFetcher.Timeout());
        _fetcher.Script("battle-pets", FakeWikiFetcher.Timeout());
        _fetcher.Script("floor-items", FakeWikiFetcher.Timeout());

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Empty(result.Pending);
    }

    [Fact]
    public async Task A_page_that_fails_three_times_alone_stops_the_pass_as_the_wiki_not_answering_well()
    {
        foreach (var slug in AllIndexes.Where(s => s != "enhancements"))
        {
            _store.SaveIndex(Index(slug), Start.AddDays(-1));
        }

        _fetcher.Script("enhancements", FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout());

        var result = await Run(Build());

        Assert.Equal(3, PageRequests.Count(r => r == "enhancements"));
        Assert.Equal(["enhancements"], result.Pending);
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("/enhancements", result.Message);
    }

    [Fact]
    public async Task The_message_for_a_timeout_names_the_page_and_says_it_took_too_long()
    {
        _fetcher.Script("pets", FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout(), FakeWikiFetcher.Timeout());

        var result = await Run(Build());

        Assert.Contains("Could not read /pets: it took too long to answer.", result.Message);
    }

    [Fact]
    public async Task A_class_index_that_cannot_be_understood_stops_the_pass_and_keeps_the_old_data()
    {
        _store.SaveIndex(Index("classes", ("old-class", "Old Class")), Start.AddDays(-30));
        _fetcher.Pages["classes"] = "<html><body><div id=\"page-content\"><p>nothing here</p></div></body></html>";

        var result = await Run(Build());

        Assert.Equal(["syfarm", "classes"], PageRequests);
        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("class index", result.Message);
        Assert.Equal("Old Class", _store.LoadTypes()!.Table.Find("old-class")!.Name);
        Assert.NotNull(_store.LoadList());
        Assert.Contains("classes", result.Pending);
    }

    [Fact]
    public async Task A_later_index_that_cannot_be_understood_is_skipped_and_the_pass_goes_on()
    {
        _fetcher.Pages["houses"] = "<html><body><p>nothing here</p></body></html>";

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("enhancements", PageRequests);
        Assert.Equal(["houses"], result.Pending);
        Assert.Contains("/houses", result.Message);
        Assert.Equal(14, _store.LoadIndexes().Count);
    }

    // --- robots.txt -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unreadable_robots_txt_stops_before_any_page_and_changes_nothing()
    {
        _fetcher.RobotsError = "The server answered HTTP 503.";

        var result = await Run(Build());

        Assert.Equal(["robots.txt"], _fetcher.Requested);
        Assert.Equal(SyncStatus.Failed, result.Status);
        Assert.Contains("robots.txt", result.Message);
        Assert.Null(_store.LoadList());
        Assert.Empty(_store.LoadIndexes());
    }

    [Theory]
    [InlineData("/syfarm")]
    [InlineData("/pets")]
    public async Task A_page_robots_txt_forbids_stops_the_pass_before_the_first_page_request(string path)
    {
        _fetcher.Robots = $"User-agent: *\nDisallow: {path}\n";

        var result = await Run(Build());

        Assert.Equal(["robots.txt"], _fetcher.Requested);
        Assert.Equal(SyncStatus.Failed, result.Status);
        Assert.Contains(path, result.Message);
        Assert.Null(_store.LoadList());
    }

    [Fact]
    public async Task A_forbidden_page_that_is_fresh_and_not_needed_does_not_matter()
    {
        _fetcher.Robots = "User-agent: *\nDisallow: /pets\n";
        _store.SaveIndex(Index("pets"), Start.AddDays(-1));

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Updated, result.Status);
    }

    // --- The list ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_suspicious_list_keeps_the_old_one_reports_it_and_still_reads_the_indexes()
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("Old", ["old-1"])], "old"), Start.AddDays(-1));
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 3)));

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("previous list was kept", result.Message);
        Assert.Equal("old", _store.LoadList()!.List.Credit);
        Assert.Equal(15, _store.LoadIndexes().Count);
    }

    [Fact]
    public async Task The_drop_guard_rejects_a_list_less_than_half_the_stored_one()
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("Old", WikiPages.Slugs("old", 138))], "old"), Start.AddDays(-1));
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", 60)));
        var updates = new List<SyncUpdate>();

        var result = await Run(Build(), updates: updates);

        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("138", result.Message);
        Assert.Equal(138, _store.LoadList()!.List.Count);
        Assert.All(updates, u => Assert.Null(u.List));
    }

    [Theory]
    [InlineData(22, 10, false)]
    [InlineData(20, 10, true)]
    [InlineData(21, 11, true)]
    [InlineData(5, 10, true)]
    public async Task The_drop_guard_compares_with_half_of_the_stored_count(int stored, int incoming, bool accepted)
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("Old", WikiPages.Slugs("old", stored))], "old"), Start.AddDays(-1));
        _fetcher.Pages["syfarm"] = WikiPages.HardFarm(("Nation", WikiPages.Slugs("nat", incoming)));
        var updates = new List<SyncUpdate>();

        await Run(Build(), updates: updates);

        Assert.Equal(accepted ? incoming : stored, _store.LoadList()!.List.Count);
        Assert.Equal(accepted, updates.Any(u => u.List is not null));
    }

    [Fact]
    public async Task A_list_page_that_cannot_be_understood_keeps_the_old_list_and_the_pass_goes_on()
    {
        _fetcher.Pages["syfarm"] = "<html><body><p>nothing</p></body></html>";

        var result = await Run(Build());

        Assert.Equal(SyncStatus.Partial, result.Status);
        Assert.Contains("Hard Farming list", result.Message);
        Assert.Equal(15, _store.LoadIndexes().Count);
    }

    // --- Saving and telling the caller as data arrives --------------------------------------------------------------

    [Fact]
    public async Task The_list_and_each_index_are_saved_at_once_and_the_caller_is_told_each_time_with_data_already_stored()
    {
        var seen = new List<(bool HasList, int Names, int StoredIndexes)>();

        await Build().RunAsync(
            null,
            u => seen.Add((u.List is not null, u.Types.Count, _store.LoadIndexes().Count)),
            CancellationToken.None);

        Assert.Equal(16, seen.Count);
        Assert.True(seen[0].HasList);
        Assert.Equal(0, seen[0].StoredIndexes);
        Assert.All(seen.Skip(1), s => Assert.False(s.HasList));
        Assert.Equal(Enumerable.Range(1, 15), seen.Skip(1).Select(s => s.StoredIndexes));
        Assert.Equal(seen.Skip(1).Select(s => s.Names).OrderBy(n => n), seen.Skip(1).Select(s => s.Names));
    }

    [Fact]
    public async Task The_update_carries_the_list_read_time_and_the_newest_index_time()
    {
        var updates = new List<SyncUpdate>();

        await Run(Build(), updates: updates);

        Assert.Equal(Start, updates[0].ListReadAtUtc);
        Assert.Equal(11, updates[0].List!.Count);
        Assert.Equal(Start, updates[^1].TypesReadAtUtc);
    }

    [Fact]
    public async Task A_caller_that_throws_does_not_break_the_pass()
    {
        var result = await Build().RunAsync(_ => throw new InvalidOperationException("ui"), _ => throw new InvalidOperationException("ui"), CancellationToken.None);

        Assert.Equal(SyncStatus.Updated, result.Status);
        Assert.Equal(15, _store.LoadIndexes().Count);
    }

    // --- Cancelling ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cancelling_keeps_what_was_saved_and_writes_nothing_more_and_lists_the_rest_as_pending()
    {
        using var cts = new CancellationTokenSource();
        _fetcher.OnFetch = slug =>
        {
            if (slug == "weapons")
            {
                cts.Cancel();
            }
        };

        var result = await Run(Build(), ct: cts.Token);

        Assert.Equal(SyncStatus.Cancelled, result.Status);
        Assert.Equal(["classes", "armors"], _store.LoadIndexes().Select(i => i.Index.IndexSlug));
        Assert.Equal(AllIndexes.Skip(2), result.Pending);
        Assert.Equal(["syfarm", "classes", "armors", "weapons"], PageRequests);
        Assert.NotNull(_store.LoadList());
    }

    [Fact]
    public async Task Cancelling_during_a_pause_ends_the_pass_at_once()
    {
        using var cts = new CancellationTokenSource();
        var delay = new CancellingDelay(cts, afterCalls: 3);

        var result = await new HardFarmSync(_fetcher, _store, delay, _clock).RunAsync(null, null, cts.Token);

        Assert.Equal(SyncStatus.Cancelled, result.Status);
        Assert.Equal(["syfarm", "classes"], PageRequests);
    }

    private sealed class CancellingDelay(CancellationTokenSource cts, int afterCalls) : AqwCompanion.App.Infrastructure.IDelay
    {
        private int _calls;

        public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            if (++_calls == afterCalls)
            {
                cts.Cancel();
            }

            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Cancelling_before_the_start_does_nothing_at_all()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Run(Build(), ct: cts.Token);

        Assert.Equal(SyncStatus.Cancelled, result.Status);
        Assert.Empty(_fetcher.Requested);
        Assert.Null(_store.LoadList());
    }

    [Fact]
    public async Task A_page_that_arrives_after_the_cancellation_is_not_saved()
    {
        using var cts = new CancellationTokenSource();
        _fetcher.OnFetch = slug =>
        {
            if (slug == "armors")
            {
                cts.Cancel();
            }
        };
        _fetcher.Scripts["armors"] = new Queue<PageFetchResult>([
            PageFetchResult.Success(PageSnapshot.Create("https://aqwwiki.wikidot.com/armors", WikiPages.Index(("a", "A")), "1", Start))]);

        var result = await Run(Build(), ct: cts.Token);

        Assert.Equal(SyncStatus.Cancelled, result.Status);
        Assert.Equal(["classes"], _store.LoadIndexes().Select(i => i.Index.IndexSlug));
    }

    // --- Never throws -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unexpected_failure_is_a_failed_result_not_an_exception()
    {
        var sync = new HardFarmSync(new ThrowingFetcher(), _store, _delay, _clock);

        var result = await sync.RunAsync(null, null, CancellationToken.None);

        Assert.Equal(SyncStatus.Failed, result.Status);
        Assert.NotEmpty(result.Message);
        Assert.DoesNotContain("secret", result.Message);
        Assert.Equal(AllIndexes, result.Pending);
    }

    private sealed class ThrowingFetcher : IWikiPageFetcher
    {
        public Task<PageFetchResult> FetchPageAsync(string url, TimeSpan? timeout = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("secret detail");

        public Task<PageFetchResult> FetchRobotsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("secret detail");
    }

    [Fact]
    public async Task A_store_that_fails_does_not_make_the_pass_throw()
    {
        _inner.ThrowOnSave = true;
        _inner.ThrowOnLoad = true;

        var result = await Run(Build());

        Assert.NotEmpty(result.Message);
        Assert.Equal(15, PageRequests.Count() - 1);
    }

    [Fact]
    public void Pending_never_throws_with_a_failing_store()
    {
        _inner.ThrowOnLoad = true;

        Assert.Equal(AllIndexes, Build().GetPending());
    }

    [Fact]
    public void The_defaults_are_the_agreed_ones()
    {
        var options = new HardFarmSyncOptions();

        Assert.Equal(TimeSpan.FromSeconds(3), options.Pause);
        Assert.Equal(TimeSpan.FromSeconds(8), options.SlowAnswer);
        Assert.Equal(TimeSpan.FromSeconds(12), options.MaxPause);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)], options.RetryWaits);
        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(3, options.MaxConsecutiveFailures);
        Assert.Equal(TimeSpan.FromDays(7), options.IndexMaxAge);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ListTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.IndexTimeout);
    }
}
