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
    private readonly FakeHardFarmUpdater _updater = new();
    private readonly InventoryCardViewModel _inventory = new(new SidebarViewModel());
    private readonly FakeUrlLauncher _launcher = new();

    public HardFarmsViewModelTests() => _store = new HardFarmStore(_inner);

    private HardFarmsViewModel Build() => new(_store, _updater, _inventory, _launcher);

    private static HardFarmList List() => new(
        [new HardFarmGroup("Nation", ["blade-one", "cape-one", "void-highlord-class"]), new HardFarmGroup("Legion", ["blade-two", "cape-one", "pet-one"])],
        "Created by Tester, Revised by Checker");

    private static WikiTypeTable Types() => new([
        new WikiTypeRow("blade-one", "Blade One", WikiType.Weapon),
        new WikiTypeRow("cape-one", "Cape One", WikiType.Cape),
        new WikiTypeRow("blade-two", "Blade Two (AC)", WikiType.Weapon),
        new WikiTypeRow("pet-one", "Pet One", WikiType.Pet)]);

    private static CharacterInventory Owns(params string[] names) =>
        new(names.Select(n => new InventoryItem(n, "Misc", 1, false, false)).ToList());

    private async Task<HardFarmsViewModel> Loaded()
    {
        _store.SaveList(List(), At);
        _store.SaveTypes(Types(), At);
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
        Assert.Equal("Click 'Update from the Wiki' to import the Hard Farming list (about 15 pages, ~45 s).", vm.EmptyStateText);
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
            [Ownership.Owned, Ownership.Owned, Ownership.Unknown, Ownership.Owned, Ownership.Missing],
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
        Assert.Equal(["cape-one", "blade-two", "pet-one"], vm.Rows.Select(r => r.Slug));

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
    public async Task Update_runs_the_updater_shows_progress_and_is_disabled_meanwhile()
    {
        var vm = Build();
        _updater.Gate = new TaskCompletionSource();
        _updater.Result = new HardFarmUpdateResult(HardFarmUpdateStatus.Updated, "Stored it.", List(), Types(), At);
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        var run = vm.UpdateCommand.ExecuteAsync(null);

        Assert.True(vm.IsUpdating);
        Assert.Equal(HardFarmsState.Updating, vm.State);
        Assert.False(vm.UpdateCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.ShowEmptyState);

        _updater.Progress!(new HardFarmProgress(2, 17, "Hard Farming list"));
        Assert.Equal("Step 2 of 17: Hard Farming list", vm.ProgressText);
        Assert.True(vm.HasProgress);

        _updater.Gate.SetResult();
        await run;

        Assert.False(vm.IsUpdating);
        Assert.True(vm.UpdateCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.ProgressText);
        Assert.Equal("Stored it.", vm.StatusText);
        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
        Assert.Equal(1, _updater.Calls);
    }

    [Fact]
    public async Task Cancel_cancels_the_token_given_to_the_updater()
    {
        var vm = Build();
        _updater.Gate = new TaskCompletionSource();
        _updater.Result = new HardFarmUpdateResult(HardFarmUpdateStatus.Cancelled, "Update cancelled.");

        var run = vm.UpdateCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        Assert.True(_updater.Token.IsCancellationRequested);
        _updater.Gate.SetResult();
        await run;

        Assert.Equal("Update cancelled.", vm.StatusText);
        Assert.Equal(HardFarmsState.Idle, vm.State);
    }

    [Fact]
    public async Task A_failed_update_keeps_the_shown_data_and_reports_the_message()
    {
        var vm = await Loaded();
        _updater.Result = new HardFarmUpdateResult(HardFarmUpdateStatus.Failed, "Could not read the Hard Farming list (HTTP 503).");

        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Could not read the Hard Farming list (HTTP 503).", vm.StatusText);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
    }

    [Fact]
    public async Task An_update_with_only_new_types_keeps_the_list_and_its_date()
    {
        var vm = await Loaded();
        _updater.Result = new HardFarmUpdateResult(
            HardFarmUpdateStatus.Partial,
            "Types only.",
            null,
            new WikiTypeTable([new WikiTypeRow("blade-one", "Blade 1", WikiType.Armor)]),
            At.AddDays(5));

        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Blade 1", vm.Rows[0].Name);
        Assert.Equal(WikiType.Armor, vm.Rows[0].Type);
        Assert.Equal(UpdatedTextFormatter.Format(At), vm.LastUpdatedText);
    }

    [Fact]
    public async Task An_updater_that_throws_still_ends_idle_with_a_message()
    {
        var vm = Build();
        _updater.Throw = true;

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

        public void SaveTypes(WikiTypeTable table, DateTimeOffset readAtUtc) => inner.SaveTypes(table, readAtUtc);
    }

    /// <summary>An updater that ignores its cancellation token; every call waits for the test to answer it.</summary>
    private sealed class StubbornUpdater : IHardFarmUpdater
    {
        public sealed record Call(TaskCompletionSource<HardFarmUpdateResult> Answer, Action<HardFarmProgress>? Progress, CancellationToken Token);

        public List<Call> Calls { get; } = [];

        public Task<HardFarmUpdateResult> UpdateAsync(Action<HardFarmProgress>? progress, CancellationToken ct)
        {
            var call = new Call(new TaskCompletionSource<HardFarmUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously), progress, ct);
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
        _updater.Result = new HardFarmUpdateResult(HardFarmUpdateStatus.Updated, "Stored.", List(), Types(), At);

        await vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.Equal(Ownership.Unknown, r.Ownership));
    }

    [Fact]
    public async Task Reset_cancels_a_running_update_and_its_late_answer_does_not_bring_the_data_back()
    {
        var updater = new StubbornUpdater();
        var vm = new HardFarmsViewModel(_store, updater, _inventory, _launcher);

        var run = vm.UpdateCommand.ExecuteAsync(null);
        Assert.True(vm.IsUpdating);
        vm.Reset();

        var call = updater.Calls.Single();
        Assert.True(call.Token.IsCancellationRequested);
        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.True(vm.ShowEmptyState);

        call.Progress!(new HardFarmProgress(3, 17, "/classes"));
        call.Answer.SetResult(new HardFarmUpdateResult(HardFarmUpdateStatus.Updated, "Stored it.", List(), Types(), At));
        await run;

        Assert.Empty(vm.Rows);
        Assert.False(vm.HasList);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.Equal(string.Empty, vm.ProgressText);
        Assert.Equal(string.Empty, vm.LastUpdatedText);
        Assert.Equal(HardFarmsState.Idle, vm.State);
        Assert.True(vm.ShowEmptyState);
    }

    [Fact]
    public async Task A_cancelled_old_run_does_not_disturb_an_update_started_after_Reset()
    {
        var updater = new StubbornUpdater();
        var vm = new HardFarmsViewModel(_store, updater, _inventory, _launcher);
        var oldRun = vm.UpdateCommand.ExecuteAsync(null);
        vm.Reset();
        var newRun = vm.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(2, updater.Calls.Count);
        Assert.True(vm.IsUpdating);

        updater.Calls[0].Answer.SetResult(new HardFarmUpdateResult(HardFarmUpdateStatus.Cancelled, "Update cancelled."));
        await oldRun;

        Assert.True(vm.IsUpdating);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.False(newRun.IsCompleted);
        Assert.False(updater.Calls[1].Token.IsCancellationRequested);
        Assert.True(vm.CancelCommand.CanExecute(null));

        updater.Calls[1].Answer.SetResult(new HardFarmUpdateResult(HardFarmUpdateStatus.Updated, "Stored.", List(), Types(), At));
        await newRun;
        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("Stored.", vm.StatusText);
    }

    [Fact]
    public async Task A_late_restore_after_Reset_does_not_bring_the_stored_data_back()
    {
        _store.SaveList(List(), At);
        var gated = new GatedStore(_store);
        var vm = new HardFarmsViewModel(gated, _updater, _inventory, _launcher);

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
        var vm = new HardFarmsViewModel(gated, _updater, _inventory, _launcher);
        var newer = new WikiTypeTable([new WikiTypeRow("blade-one", "Blade 1", WikiType.Armor)]);
        _updater.Result = new HardFarmUpdateResult(HardFarmUpdateStatus.Partial, "Types only.", null, newer, At.AddDays(5));

        var restore = vm.RestoreAsync();
        await gated.Entered.Task;
        await vm.UpdateCommand.ExecuteAsync(null);
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
        var vm = new HardFarmsViewModel(gated, _updater, _inventory, _launcher);
        _updater.Result = new HardFarmUpdateResult(HardFarmUpdateStatus.Updated, "Stored.", List(), Types(), At);

        var restore = vm.RestoreAsync();
        await gated.Entered.Task;
        await vm.UpdateCommand.ExecuteAsync(null);
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
        _updater.Result = new HardFarmUpdateResult(HardFarmUpdateStatus.Updated, "Stored.", List(), Types(), At);

        var ex = await Record.ExceptionAsync(() => vm.UpdateCommand.ExecuteAsync(null));

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
}

