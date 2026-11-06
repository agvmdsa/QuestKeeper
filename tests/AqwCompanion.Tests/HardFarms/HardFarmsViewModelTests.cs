using System.ComponentModel;
using AqwCompanion.App.Account;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmsViewModelTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly InMemoryStoredDataStore _inner = new();
    private readonly HardFarmStore _store;
    private readonly FakeHardFarmSync _sync = new();
    private readonly InventoryCardViewModel _inventory = new(new SidebarViewModel());
    private readonly FakeUrlLauncher _launcher = new();

    public HardFarmsViewModelTests() => _store = new HardFarmStore(_inner);

    private HardFarmsViewModel Build() => new(_store, _sync, _inventory, _launcher);

    private static HardFarmList List() => new(
        [new HardFarmGroup("Nation", ["blade-one", "cape-one", "void-highlord-class"]), new HardFarmGroup("Legion", ["blade-two", "cape-one", "pet-one"])],
        "Created by Tester, Revised by Checker");

    private static WikiIndex[] Indexes() =>
    [
        new(WikiUrls.WeaponsSlug, WikiType.Weapon, [new WikiIndexEntry("blade-one", "Blade One"), new WikiIndexEntry("blade-two", "Blade Two (AC)")]),
        new(WikiUrls.CapesSlug, WikiType.Cape, [new WikiIndexEntry("cape-one", "Cape One")]),
        new(WikiUrls.PetsSlug, WikiType.Pet, [new WikiIndexEntry("pet-one", "Pet One")]),
    ];

    private static WikiTypeTable Types() => WikiTypeTable.From(Indexes());

    private void SaveTypes()
    {
        foreach (var index in Indexes())
        {
            _store.SaveIndex(index, At);
        }
    }

    private static CharacterInventory Owns(params string[] names) =>
        new(names.Select(n => new InventoryItem(n, "Misc", 1, false, false)).ToList());

    private async Task<HardFarmsViewModel> Loaded()
    {
        _store.SaveList(List(), At);
        SaveTypes();
        var vm = Build();
        await vm.RestoreAsync();
        return vm;
    }

    [Fact]
    public void Starts_idle_and_empty_with_the_empty_state_shown()
    {
        var vm = Build();

        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.Empty(vm.Rows);
        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.HasList);
        Assert.Equal(
            "Click 'Update from the Wiki' to import the Hard Farming list. The list shows up in a few seconds; the item types keep arriving for a minute or two.",
            vm.EmptyStateText);
        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
        Assert.False(vm.ShowPending);
        Assert.Equal(string.Empty, vm.LastUpdatedText);
        Assert.Equal(string.Empty, vm.SummaryText);
        Assert.True(vm.UpdateCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restore_shows_the_stored_list_in_group_then_list_order_with_distinct_entries()
    {
        var vm = await Loaded();

        Assert.Equal(
            ["blade-one", "cape-one", "void-highlord-class", "blade-two", "pet-one"],
            vm.Rows.Select(r => r.Slug));
        Assert.Equal(["Nation", "Nation", "Nation", "Legion", "Legion"], vm.Rows.Select(r => r.Group));
        Assert.False(vm.ShowEmptyState);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
        Assert.Contains(LocalTimeText.Format(At), vm.LastUpdatedText);
    }

    [Fact]
    public async Task Rows_carry_the_name_type_wiki_url_and_the_fallback_name_for_unknown_slugs()
    {
        var vm = await Loaded();

        var blade = vm.Rows[0];
        Assert.Equal(new HardFarmRow("blade-one", "Blade One", WikiType.Weapon, "Nation", Ownership.Unknown, "https://aqwwiki.wikidot.com/blade-one"), blade);
        var unknown = vm.Rows.Single(r => r.Slug == "void-highlord-class");
        Assert.Equal("Void Highlord Class", unknown.Name);
        Assert.Equal(WikiType.Other, unknown.Type);
        Assert.Equal(Ownership.Unknown, unknown.Ownership);
    }

    [Fact]
    public void Prettify_title_cases_the_slug_words()
    {
        Assert.Equal("Void Highlord Class", HardFarmsViewModel.Prettify("void-highlord-class"));
        Assert.Equal("A B", HardFarmsViewModel.Prettify("a--b-"));
    }

    [Fact]
    public async Task Restore_works_without_a_type_table_and_does_nothing_without_a_list()
    {
        var empty = Build();
        await empty.RestoreAsync();
        Assert.Empty(empty.Rows);

        _store.SaveList(List(), At);
        var vm = Build();
        await vm.RestoreAsync();
        Assert.Equal(5, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.Equal(WikiType.Other, r.Type));
        Assert.Equal("Blade One", vm.Rows[0].Name);
    }

    [Fact]
    public async Task Restore_survives_a_failing_store()
    {
        _inner.ThrowOnLoad = true;
        var vm = Build();

        await vm.RestoreAsync();

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task The_credit_line_names_the_author_and_the_license()
    {
        var vm = await Loaded();

        Assert.Equal("List by Tester, Revised by Checker · AQW Wiki (CC BY-SA 3.0)", vm.CreditText);
        Assert.True(vm.HasCredit);
    }

    [Theory]
    [InlineData("Sy", "List by Sy · AQW Wiki (CC BY-SA 3.0)")]
    [InlineData("created by Sy", "List by Sy · AQW Wiki (CC BY-SA 3.0)")]
    [InlineData(null, "Hard Farming list · AQW Wiki (CC BY-SA 3.0)")]
    [InlineData("  ", "Hard Farming list · AQW Wiki (CC BY-SA 3.0)")]
    public async Task The_credit_line_handles_the_credit_forms(string? credit, string expected)
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("G", ["a"])], credit), At);
        var vm = Build();

        await vm.RestoreAsync();

        Assert.Equal(expected, vm.CreditText);
    }

    [Fact]
    public async Task Ownership_follows_the_inventory_and_is_recomputed_when_it_changes()
    {
        var vm = await Loaded();
        Assert.All(vm.Rows, r => Assert.Equal(Ownership.Unknown, r.Ownership));

        _inventory.Data = Owns("Blade One", "Blade Two (AC)", "Cape One");

        Assert.Equal(
            [Ownership.Owned, Ownership.Owned, Ownership.Missing, Ownership.Owned, Ownership.Missing],
            vm.Rows.Select(r => r.Ownership));

        _inventory.Data = Owns("Pet One");
        Assert.Equal(Ownership.Owned, vm.Rows.Single(r => r.Slug == "pet-one").Ownership);
        Assert.Equal(Ownership.Missing, vm.Rows.Single(r => r.Slug == "blade-one").Ownership);

        _inventory.Data = null;
        Assert.All(vm.Rows, r => Assert.Equal(Ownership.Unknown, r.Ownership));
    }

    [Fact]
    public async Task An_inventory_loaded_before_the_list_is_used_at_once()
    {
        _inventory.Data = Owns("Blade One");

        var vm = await Loaded();

        Assert.Equal(Ownership.Owned, vm.Rows[0].Ownership);
        Assert.Equal(Ownership.Missing, vm.Rows[1].Ownership);
    }

    [Fact]
    public async Task The_summary_counts_owned_over_the_type_and_group_filters_only()
    {
        var vm = await Loaded();
        _inventory.Data = Owns("Blade One", "Cape One");
        Assert.Equal("2 of 5 owned", vm.SummaryText);

        vm.TypeFilter = vm.TypeOptions.Single(o => o.Value == WikiType.Weapon);
        Assert.Equal("1 of 2 owned", vm.SummaryText);

        vm.GroupFilter = vm.GroupOptions.Single(o => o.Name == "Legion");
        Assert.Equal("0 of 1 owned", vm.SummaryText);

        // Search and the owned filter narrow the list but not the summary.
        vm.GroupFilter = vm.GroupOptions[0];
        vm.OwnedFilter = vm.OwnedOptions.Single(o => o.Value == Ownership.Owned);
        vm.SearchText = "one";
        Assert.Equal("1 of 2 owned", vm.SummaryText);
        Assert.Equal(["blade-one"], vm.Rows.Select(r => r.Slug));
    }

    [Fact]
    public async Task Filters_narrow_the_rows_and_search_ignores_case_and_edges()
    {
        var vm = await Loaded();
        _inventory.Data = Owns("Blade One");

        vm.TypeFilter = vm.TypeOptions.Single(o => o.Value == WikiType.Cape);
        Assert.Equal(["cape-one"], vm.Rows.Select(r => r.Slug));

        vm.TypeFilter = HardFarmsViewModel.AllTypes;
        vm.GroupFilter = vm.GroupOptions.Single(o => o.Name == "Legion");
        Assert.Equal(["blade-two", "pet-one"], vm.Rows.Select(r => r.Slug));

        vm.GroupFilter = HardFarmsViewModel.AllGroups;
        vm.OwnedFilter = vm.OwnedOptions.Single(o => o.Value == Ownership.Missing);
        Assert.Equal(["cape-one", "void-highlord-class", "blade-two", "pet-one"], vm.Rows.Select(r => r.Slug));

        vm.OwnedFilter = HardFarmsViewModel.AllOwnership;
        vm.SearchText = "  BLADE ";
        Assert.Equal(["blade-one", "blade-two"], vm.Rows.Select(r => r.Slug));

        vm.SearchText = "zzz";
        Assert.Empty(vm.Rows);
        Assert.True(vm.ShowNoMatches);
    }

    [Fact]
    public async Task The_option_lists_have_all_plus_the_choices()
    {
        var vm = await Loaded();

        Assert.Equal(["All types", "Class", "Armor", "Weapon", "Helm", "Cape", "Pet", "House", "Other"], vm.TypeOptions.Select(o => o.Label));
        Assert.Equal(["All groups", "Nation", "Legion"], vm.GroupOptions.Select(o => o.Label));
        Assert.Equal(["All", "Missing", "Owned", "Unknown"], vm.OwnedOptions.Select(o => o.Label));
        Assert.Same(HardFarmsViewModel.AllTypes, vm.TypeFilter);
        Assert.Same(HardFarmsViewModel.AllGroups, vm.GroupFilter);
        Assert.Same(HardFarmsViewModel.AllOwnership, vm.OwnedFilter);
    }

    [Fact]
    public async Task Update_runs_the_sync_shows_progress_and_is_disabled_meanwhile()
    {
        var vm = Build();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored it.", []);
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        var run = vm.UpdateCommand.ExecuteAsync(null);

        Assert.True(vm.IsUpdating);
        Assert.Equal(HardFarmsState.Updating, vm.State);
        Assert.False(vm.UpdateCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.ShowEmptyState);

        _sync.Progress!(new SyncProgress(0, 15, "Hard Farming list", null));
        Assert.Equal("Hard Farming list", vm.ProgressText);
        Assert.True(vm.HasProgress);

        _sync.Saved!(new SyncUpdate(List(), At, Types(), At));
        _sync.Gate.SetResult();
        await run;

        Assert.False(vm.IsUpdating);
        Assert.True(vm.UpdateCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.ProgressText);
        Assert.Equal("Stored it.", vm.StatusText);
        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
        Assert.Equal(1, _sync.Calls);
    }

    [Fact]
    public async Task Cancel_cancels_the_token_given_to_the_sync_and_keeps_what_was_saved()
    {
        var vm = Build();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Cancelled, "Update cancelled. 14 pages pending.", WikiUrls.TypeIndexSlugs.Skip(1).ToList());

        var run = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(List(), At, WikiTypeTable.From(Indexes().Take(1)), At));
        vm.CancelCommand.Execute(null);
        Assert.True(_sync.Token.IsCancellationRequested);
        _sync.Gate.SetResult();
        await run;

        Assert.Equal("Update cancelled. 14 pages pending.", vm.StatusText);
        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Blade One", vm.Rows[0].Name);
        Assert.Equal("Continue", vm.UpdateButtonText);
        Assert.Equal("14 pages pending — Continue", vm.PendingText);
        Assert.True(vm.ShowPending);
    }

    [Fact]
    public async Task A_failed_update_keeps_the_shown_data_and_reports_the_message()
    {
        var vm = await Loaded();
        _sync.Result = new SyncResult(SyncStatus.Failed, "Could not read the Hard Farming list (HTTP 503).", []);

        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Could not read the Hard Farming list (HTTP 503).", vm.StatusText);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
    }

    [Fact]
    public async Task An_update_with_only_new_types_keeps_the_list_and_its_date()
    {
        var vm = await Loaded();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Partial, "Types only.", ["pets"]);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(
            null,
            null,
            new WikiTypeTable([new WikiTypeRow("blade-one", "Blade 1", WikiType.Armor)]),
            At.AddDays(5)));
        _sync.Gate.SetResult();
        await run;

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Blade 1", vm.Rows[0].Name);
        Assert.Equal(WikiType.Armor, vm.Rows[0].Type);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
    }

    [Fact]
    public async Task A_sync_that_throws_still_ends_idle_with_a_message()
    {
        var vm = Build();
        _sync.Throw = true;

        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task Open_on_the_wiki_launches_only_https_wiki_urls()
    {
        var vm = await Loaded();

        vm.OpenOnWikiCommand.Execute(vm.Rows[0]);
        vm.OpenOnWikiCommand.Execute(vm.Rows[0] with { WikiUrl = "http://aqwwiki.wikidot.com/x" });
        vm.OpenOnWikiCommand.Execute(vm.Rows[0] with { WikiUrl = "https://evil.example.com/x" });
        vm.OpenOnWikiCommand.Execute(vm.Rows[0] with { WikiUrl = "file:///etc/passwd" });
        vm.OpenOnWikiCommand.Execute(vm.Rows[0] with { WikiUrl = "https://aqwwiki.wikidot.com/a?x=1" });
        vm.OpenOnWikiCommand.Execute(vm.Rows[0] with { WikiUrl = "https://aqwwiki.wikidot.com/a%0D%0Ab" });
        vm.OpenOnWikiCommand.Execute(null);

        Assert.Equal(["https://aqwwiki.wikidot.com/blade-one"], _launcher.Opened);
    }

    [Fact]
    public void The_process_launcher_refuses_anything_but_wiki_urls_without_starting_a_process()
    {
        var launcher = new ProcessUrlLauncher();

        Assert.False(launcher.Open("https://evil.example.com/x"));
        Assert.False(launcher.Open("file:///etc/passwd"));
        Assert.False(launcher.Open("http://aqwwiki.wikidot.com/x"));
        Assert.False(launcher.Open("https://aqwwiki.wikidot.com/a?b"));
        Assert.False(launcher.Open("https://aqwwiki.wikidot.com/a#b"));
        Assert.False(launcher.Open("https://aqwwiki.wikidot.com/a%3Fb"));
        Assert.False(launcher.Open("https://aqwwiki.wikidot.com/a\r\nb"));
        Assert.False(launcher.Open("https://aqwwiki.wikidot.com/"));
        Assert.False(launcher.Open("https://aqwwiki.wikidot.com/A"));
        Assert.False(launcher.Open("https://aqwwiki.wikidot.com/a/b"));
    }

    [Fact]
    public async Task A_row_list_of_many_entries_filters_without_rebuilding_the_full_list()
    {
        var slugs = Enumerable.Range(1, 3000).Select(i => $"item-{i}").ToList();
        _store.SaveList(new HardFarmList([new HardFarmGroup("Big", slugs)], null), At);
        var vm = Build();
        await vm.RestoreAsync();

        vm.SearchText = "item 299";

        Assert.Equal(["item-299", "item-2990", "item-2991", "item-2992", "item-2993", "item-2994", "item-2995", "item-2996", "item-2997", "item-2998", "item-2999"], vm.Rows.Select(r => r.Slug));
    }

    // --- Reset (the account's data was forgotten) ---------------------------------------------------------------

    private sealed class GatedStore(IHardFarmStore inner) : IHardFarmStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StoredHardFarmList? LoadList()
        {
            var result = inner.LoadList();
            var types = inner.LoadTypes();
            Entered.TrySetResult();
            Release.Task.Wait();
            return result;
        }

        public StoredTypeTable? LoadTypes() => inner.LoadTypes();

        public void SaveList(HardFarmList list, DateTimeOffset readAtUtc) => inner.SaveList(list, readAtUtc);

        public IReadOnlyList<StoredIndex> LoadIndexes() => inner.LoadIndexes();

        public void SaveIndex(WikiIndex index, DateTimeOffset readAtUtc) => inner.SaveIndex(index, readAtUtc);
    }

    /// <summary>A sync that ignores its cancellation token; every call waits for the test to answer it.</summary>
    private sealed class StubbornSync : IHardFarmSync
    {
        public sealed record Call(
            TaskCompletionSource<SyncResult> Answer,
            Action<SyncProgress>? Progress,
            Action<SyncUpdate>? Saved,
            CancellationToken Token);

        public List<Call> Calls { get; } = [];

        public IReadOnlyList<string> GetPending() => [];

        public Task<SyncResult> RunAsync(Action<SyncProgress>? progress, Action<SyncUpdate>? saved, CancellationToken ct)
        {
            var call = new Call(new TaskCompletionSource<SyncResult>(TaskCreationOptions.RunContinuationsAsynchronously), progress, saved, ct);
            Calls.Add(call);
            return call.Answer.Task;
        }
    }

    [Fact]
    public async Task Reset_returns_to_the_empty_state_with_unfiltered_options_and_no_texts()
    {
        var vm = await Loaded();
        _inventory.Data = Owns("Blade One");
        vm.TypeFilter = vm.TypeOptions.Single(o => o.Value == WikiType.Weapon);
        vm.GroupFilter = vm.GroupOptions.Single(o => o.Name == "Legion");
        vm.OwnedFilter = vm.OwnedOptions.Single(o => o.Value == Ownership.Missing);
        vm.SearchText = "blade";
        Assert.NotEmpty(vm.CreditText);

        vm.Reset();

        Assert.Empty(vm.Rows);
        Assert.False(vm.HasList);
        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowNoMatches);
        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.Same(HardFarmsViewModel.AllTypes, vm.TypeFilter);
        Assert.Same(HardFarmsViewModel.AllGroups, vm.GroupFilter);
        Assert.Same(HardFarmsViewModel.AllOwnership, vm.OwnedFilter);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal([HardFarmsViewModel.AllGroups], vm.GroupOptions);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.Equal(string.Empty, vm.ProgressText);
        Assert.Equal(string.Empty, vm.LastUpdatedText);
        Assert.Equal(string.Empty, vm.CreditText);
        Assert.False(vm.HasCredit);
        Assert.Equal(string.Empty, vm.SummaryText);
        Assert.Equal(HardFarmsViewModel.EmptyText, vm.EmptyStateText);
        Assert.True(vm.UpdateCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task After_Reset_a_new_list_shows_with_unknown_ownership_until_an_inventory_is_read()
    {
        var vm = await Loaded();
        _inventory.Data = Owns("Blade One");
        vm.Reset();
        _inventory.Data = null;
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored.", []);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(List(), At, Types(), At));
        _sync.Gate.SetResult();
        await run;

        Assert.Equal(5, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.Equal(Ownership.Unknown, r.Ownership));
    }

    [Fact]
    public async Task An_update_that_changes_no_row_keeps_the_same_rows_instance_and_raises_no_change_for_it()
    {
        var vm = await Loaded();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored.", []);
        var rows = vm.Rows;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(List(), At, Types(), At));
        _sync.Gate.SetResult();
        await run;

        Assert.Same(rows, vm.Rows);
        Assert.DoesNotContain(nameof(HardFarmsViewModel.Rows), changed);
    }

    [Fact]
    public async Task An_update_that_changes_a_row_replaces_the_rows()
    {
        var vm = await Loaded();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored.", []);
        var rows = vm.Rows;
        var renamed = new WikiIndex(WikiUrls.PetsSlug, WikiType.Pet, [new WikiIndexEntry("pet-one", "Pet One Renamed")]);
        var types = WikiTypeTable.From([.. Indexes().Take(2), renamed]);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(List(), At, types, At));
        _sync.Gate.SetResult();
        await run;

        Assert.NotSame(rows, vm.Rows);
        Assert.Contains(vm.Rows, r => r.Name == "Pet One Renamed");
    }

    [Fact]
    public async Task Reset_cancels_a_running_update_and_its_late_callbacks_and_answer_do_not_bring_the_data_back()
    {
        var sync = new StubbornSync();
        var vm = new HardFarmsViewModel(_store, sync, _inventory, _launcher);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        Assert.True(vm.IsUpdating);
        vm.Reset();

        var call = sync.Calls.Single();
        Assert.True(call.Token.IsCancellationRequested);
        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.True(vm.ShowEmptyState);

        call.Progress!(new SyncProgress(2, 15, "Index 3 of 15 · /weapons", null));
        call.Saved!(new SyncUpdate(List(), At, Types(), At));
        call.Answer.SetResult(new SyncResult(SyncStatus.Partial, "Stored it. 3 pages pending.", ["pets"]));
        await run;

        Assert.Empty(vm.Rows);
        Assert.False(vm.HasList);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.Equal(string.Empty, vm.ProgressText);
        Assert.Equal(string.Empty, vm.LastUpdatedText);
        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowPending);
        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
    }

    [Fact]
    public async Task A_cancelled_old_run_does_not_disturb_an_update_started_after_Reset()
    {
        var sync = new StubbornSync();
        var vm = new HardFarmsViewModel(_store, sync, _inventory, _launcher);
        var oldRun = vm.UpdateCommand.ExecuteAsync(null);
        vm.Reset();
        var newRun = vm.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(2, sync.Calls.Count);
        Assert.True(vm.IsUpdating);

        sync.Calls[0].Saved!(new SyncUpdate(List(), At, Types(), At));
        sync.Calls[0].Answer.SetResult(new SyncResult(SyncStatus.Cancelled, "Update cancelled.", []));
        await oldRun;

        Assert.True(vm.IsUpdating);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.Empty(vm.Rows);
        Assert.False(newRun.IsCompleted);
        Assert.False(sync.Calls[1].Token.IsCancellationRequested);
        Assert.True(vm.CancelCommand.CanExecute(null));

        sync.Calls[1].Saved!(new SyncUpdate(List(), At, Types(), At));
        sync.Calls[1].Answer.SetResult(new SyncResult(SyncStatus.Updated, "Stored.", []));
        await newRun;
        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Stored.", vm.StatusText);
    }

    [Fact]
    public async Task A_late_restore_after_Reset_does_not_bring_the_stored_data_back()
    {
        _store.SaveList(List(), At);
        var gated = new GatedStore(_store);
        var vm = new HardFarmsViewModel(gated, _sync, _inventory, _launcher);

        var restore = vm.RestoreAsync();
        await gated.Entered.Task;
        vm.Reset();
        gated.Release.SetResult();
        await restore;

        Assert.Empty(vm.Rows);
        Assert.False(vm.HasList);
        Assert.True(vm.ShowEmptyState);
    }

    [Fact]
    public async Task A_late_restore_does_not_overwrite_the_types_of_an_update_that_finished_meanwhile()
    {
        _store.SaveList(List(), At);
        var gated = new GatedStore(_store);
        var vm = new HardFarmsViewModel(gated, _sync, _inventory, _launcher);
        var newer = new WikiTypeTable([new WikiTypeRow("blade-one", "Blade 1", WikiType.Armor)]);
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Partial, "Types only.", ["pets"]);

        var restore = vm.RestoreAsync();
        await gated.Entered.Task;
        var run = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(null, null, newer, At.AddDays(5)));
        _sync.Gate.SetResult();
        await run;
        gated.Release.SetResult();
        await restore;

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Blade 1", vm.Rows[0].Name);
        Assert.Equal(WikiType.Armor, vm.Rows[0].Type);
        Assert.Equal(WikiType.Other, vm.Rows.Single(r => r.Slug == "pet-one").Type);
    }

    [Fact]
    public async Task A_late_restore_does_not_replace_a_list_an_update_stored_meanwhile()
    {
        var oldList = new HardFarmList([new HardFarmGroup("Old", ["old-one"])], null);
        _store.SaveList(oldList, At.AddDays(-9));
        var gated = new GatedStore(_store);
        var vm = new HardFarmsViewModel(gated, _sync, _inventory, _launcher);
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored.", []);

        var restore = vm.RestoreAsync();
        await gated.Entered.Task;
        var run = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(List(), At, Types(), At));
        _sync.Gate.SetResult();
        await run;
        gated.Release.SetResult();
        await restore;

        Assert.Equal(5, vm.Rows.Count);
        Assert.DoesNotContain(vm.Rows, r => r.Slug == "old-one");
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
    }

    // --- Selections cleared by the UI, and the summary ------------------------------------------------------------

    [Fact]
    public async Task A_cleared_selection_counts_as_All_and_never_throws()
    {
        var vm = await Loaded();
        vm.GroupFilter = vm.GroupOptions.Single(o => o.Name == "Legion");

        var ex = Record.Exception(() =>
        {
            vm.GroupFilter = null!;
            vm.TypeFilter = null!;
            vm.OwnedFilter = null!;
        });

        Assert.Null(ex);
        Assert.Same(HardFarmsViewModel.AllGroups, vm.GroupFilter);
        Assert.Same(HardFarmsViewModel.AllTypes, vm.TypeFilter);
        Assert.Same(HardFarmsViewModel.AllOwnership, vm.OwnedFilter);
        Assert.Equal(5, vm.Rows.Count);
    }

    [Fact]
    public async Task Replacing_the_group_options_while_the_selection_is_cleared_keeps_the_chosen_group()
    {
        var vm = await Loaded();
        vm.GroupFilter = vm.GroupOptions.Single(o => o.Name == "Legion");
        // What a ComboBox does when its items are replaced: the bound selection is written back as null.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HardFarmsViewModel.GroupOptions))
            {
                vm.GroupFilter = null!;
            }
        };
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored.", []);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        var ex = Record.Exception(() => _sync.Saved!(new SyncUpdate(List(), At, Types(), At)));
        _sync.Gate.SetResult();
        await run;

        Assert.Null(ex);
        Assert.Equal("Legion", vm.GroupFilter.Name);
        Assert.Equal(["blade-two", "pet-one"], vm.Rows.Select(r => r.Slug));
    }

    [Fact]
    public async Task Without_an_inventory_the_summary_counts_entries_and_asks_for_a_refresh()
    {
        var vm = await Loaded();

        Assert.Equal("5 entries — refresh your account to see what you own", vm.SummaryText);
        Assert.Equal("refresh your account to see what you own", HardFarmsViewModel.NoInventoryHint);

        vm.TypeFilter = vm.TypeOptions.Single(o => o.Value == WikiType.Pet);
        Assert.Equal("1 entry — refresh your account to see what you own", vm.SummaryText);

        _inventory.Data = Owns("Pet One");
        Assert.Equal("1 of 1 owned", vm.SummaryText);

        _inventory.Data = null;
        Assert.Equal("1 entry — refresh your account to see what you own", vm.SummaryText);
    }

    // --- Button, pending line and progress texts (AQW-0019) ---------------------------------------------------------

    private static readonly string[] ThreePending = ["pets", "houses", "grounds"];

    [Fact]
    public async Task Nothing_ever_read_says_Update_from_the_Wiki_even_though_every_page_is_pending()
    {
        var vm = Build();
        _sync.Pending = [.. WikiUrls.TypeIndexSlugs];
        await vm.RestoreAsync();

        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
        Assert.Equal("Update the Hard Farming list from the Wiki", vm.UpdateButtonAccessibleName);
        Assert.False(vm.CanContinue);
        Assert.False(vm.ShowPending);
    }

    [Fact]
    public async Task A_failed_first_pass_that_stored_nothing_still_says_Update_from_the_Wiki()
    {
        var vm = Build();
        _sync.Result = new SyncResult(SyncStatus.Failed, "Could not read the Hard Farming list.", [.. WikiUrls.TypeIndexSlugs]);

        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
        Assert.False(vm.ShowPending);
        Assert.Equal("Could not read the Hard Farming list.", vm.StatusText);
    }

    [Fact]
    public async Task A_stored_list_with_pages_pending_says_Continue_with_the_count_after_a_restore()
    {
        _sync.Pending = ThreePending;
        var vm = await Loaded();

        Assert.Equal("Continue", vm.UpdateButtonText);
        Assert.Equal("Continue reading the pending Wiki pages", vm.UpdateButtonAccessibleName);
        Assert.Equal("3 pages pending — Continue", vm.PendingText);
        Assert.True(vm.ShowPending);
    }

    [Fact]
    public async Task One_pending_page_is_not_pluralised()
    {
        _sync.Pending = ["pets"];
        var vm = await Loaded();

        Assert.Equal("1 page pending — Continue", vm.PendingText);
    }

    [Fact]
    public async Task A_stored_list_with_nothing_pending_says_Update_from_the_Wiki_and_hides_the_pending_line()
    {
        _sync.Pending = [];
        var vm = await Loaded();

        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
        Assert.False(vm.ShowPending);
    }

    [Fact]
    public async Task The_pending_line_is_hidden_while_a_pass_runs()
    {
        _sync.Pending = ThreePending;
        var vm = await Loaded();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored.", []);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        Assert.False(vm.ShowPending);
        _sync.Gate.SetResult();
        await run;

        Assert.False(vm.ShowPending);
        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
    }

    [Fact]
    public async Task A_partial_pass_leaves_Continue_and_Continue_runs_the_sync_again_until_nothing_is_pending()
    {
        var vm = Build();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Partial, "Stored 11 entries in the list and 12 index pages. 3 pages pending.", ThreePending);
        var first = vm.UpdateCommand.ExecuteAsync(null);
        _sync.Saved!(new SyncUpdate(List(), At, Types(), At));
        _sync.Gate.SetResult();
        await first;

        Assert.Equal("Continue", vm.UpdateButtonText);
        Assert.Equal("3 pages pending — Continue", vm.PendingText);
        Assert.Equal("Stored 11 entries in the list and 12 index pages. 3 pages pending.", vm.StatusText);
        Assert.True(vm.UpdateCommand.CanExecute(null));

        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored 11 entries in the list and 15 index pages.", []);
        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(2, _sync.Calls);
        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
        Assert.False(vm.ShowPending);
        Assert.Equal("Stored 11 entries in the list and 15 index pages.", vm.StatusText);
    }

    [Fact]
    public async Task A_complete_pass_says_Update_from_the_Wiki_and_shows_the_final_message()
    {
        var vm = Build();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Stored 11 entries in the list and 15 index pages.", []);

        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
        Assert.Equal("Stored 11 entries in the list and 15 index pages.", vm.StatusText);
        Assert.Equal(string.Empty, vm.ProgressText);
    }

    [Fact]
    public async Task Progress_shows_the_label_and_the_waiting_text_when_there_is_one()
    {
        var vm = Build();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Done.", []);
        var run = vm.UpdateCommand.ExecuteAsync(null);
        Assert.Equal("Starting...", vm.ProgressText);

        _sync.Progress!(new SyncProgress(5, 15, "Index 6 of 15 · /pets", null));
        Assert.Equal("Index 6 of 15 · /pets", vm.ProgressText);

        _sync.Progress!(new SyncProgress(5, 15, "Index 6 of 15 · /pets", "Waiting 10 s before trying /pets again"));
        Assert.Equal("Index 6 of 15 · /pets · Waiting 10 s before trying /pets again", vm.ProgressText);

        _sync.Progress!(new SyncProgress(5, 15, "Index 6 of 15 · /pets", null));
        Assert.Equal("Index 6 of 15 · /pets", vm.ProgressText);

        _sync.Gate.SetResult();
        await run;
        Assert.Equal(string.Empty, vm.ProgressText);
    }

    [Fact]
    public async Task Rows_fill_in_as_updates_arrive_and_the_filters_the_search_and_the_group_are_kept()
    {
        var vm = Build();
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Done.", []);
        var run = vm.UpdateCommand.ExecuteAsync(null);
        var all = Indexes();

        _sync.Saved!(new SyncUpdate(List(), At, WikiTypeTable.Empty, null));
        Assert.Equal(5, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.Equal(WikiType.Other, r.Type));
        Assert.Equal(["All groups", "Nation", "Legion"], vm.GroupOptions.Select(o => o.Label));

        vm.GroupFilter = vm.GroupOptions.Single(o => o.Name == "Legion");
        vm.SearchText = "o";
        Assert.Equal(["blade-two", "pet-one"], vm.Rows.Select(r => r.Slug));

        _sync.Saved!(new SyncUpdate(null, null, WikiTypeTable.From(all.Take(1)), At.AddHours(1)));
        Assert.Equal("Legion", vm.GroupFilter.Name);
        Assert.Equal("o", vm.SearchText);
        Assert.Equal(["blade-two", "pet-one"], vm.Rows.Select(r => r.Slug));
        Assert.Equal(WikiType.Weapon, vm.Rows[0].Type);
        Assert.Equal(WikiType.Other, vm.Rows[1].Type);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);

        vm.TypeFilter = vm.TypeOptions.Single(o => o.Value == WikiType.Pet);
        Assert.Empty(vm.Rows);
        _sync.Saved!(new SyncUpdate(null, null, WikiTypeTable.From(all), At.AddHours(2)));
        Assert.Equal(["pet-one"], vm.Rows.Select(r => r.Slug));
        Assert.Same(vm.TypeOptions.Single(o => o.Value == WikiType.Pet), vm.TypeFilter);

        _sync.Gate.SetResult();
        await run;
        Assert.Equal(["pet-one"], vm.Rows.Select(r => r.Slug));
    }

    [Fact]
    public async Task What_the_sync_reports_goes_through_the_UI_thread_hook_and_a_late_one_after_Reset_does_nothing()
    {
        var queue = new Queue<Action>();
        var vm = new HardFarmsViewModel(_store, _sync, _inventory, _launcher, queue.Enqueue);
        _sync.Gate = new TaskCompletionSource();
        _sync.Result = new SyncResult(SyncStatus.Updated, "Done.", []);
        var run = vm.UpdateCommand.ExecuteAsync(null);

        await Task.Run(() =>
        {
            _sync.Progress!(new SyncProgress(0, 15, "robots.txt", null));
            _sync.Saved!(new SyncUpdate(List(), At, Types(), At));
        });

        Assert.Equal("Starting...", vm.ProgressText);
        Assert.Empty(vm.Rows);
        Assert.Equal(2, queue.Count);

        queue.Dequeue()();
        queue.Dequeue()();

        Assert.Equal("robots.txt", vm.ProgressText);
        Assert.Equal(5, vm.Rows.Count);

        await Task.Run(() => _sync.Saved!(new SyncUpdate(null, null, WikiTypeTable.Empty, null)));
        vm.Reset();
        queue.Dequeue()();
        _sync.Gate.SetResult();
        await run;

        Assert.Empty(vm.Rows);
        Assert.False(vm.HasList);
    }

    [Fact]
    public async Task Reset_forgets_the_pending_pages_and_goes_back_to_Update_from_the_Wiki()
    {
        _sync.Pending = ThreePending;
        var vm = await Loaded();
        Assert.True(vm.ShowPending);

        vm.Reset();

        Assert.False(vm.ShowPending);
        Assert.Equal("Update from the Wiki", vm.UpdateButtonText);
    }

    [Fact]
    public async Task Entries_the_owner_owns_show_Owned_with_a_loaded_inventory_and_no_wiki_name_yet()
    {
        _store.SaveList(
            new HardFarmList([new HardFarmGroup("Nation", ["evolved-shadow-of-nulgath", "evolved-legacy-of-nulgath-quest", "void-paladin", "cyber-void-paladin"])], null),
            At);
        _inventory.Data = Owns("Evolved Shadow of Nulgath", "Evolved Legacy of Nulgath", "Void Paladin");
        var vm = Build();

        await vm.RestoreAsync();

        Assert.Equal(
            [Ownership.Owned, Ownership.Owned, Ownership.Owned, Ownership.Missing],
            vm.Rows.Select(r => r.Ownership));
        Assert.Equal("3 of 4 owned", vm.SummaryText);
    }

    [Fact]
    public async Task Without_an_inventory_every_row_is_Unknown_even_with_a_matching_name()
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["void-paladin"])], null), At);
        var vm = Build();

        await vm.RestoreAsync();

        Assert.Equal([Ownership.Unknown], vm.Rows.Select(r => r.Ownership));
    }
}
