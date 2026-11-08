using System.Diagnostics;
using AqwCompanion.App;
using AqwCompanion.App.Account;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmsViewTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private sealed class Host
    {
        public required HardFarmsViewModel Vm { get; init; }

        public required HardFarmsView View { get; init; }

        public required Window Window { get; init; }

        public required FakeHardFarmSync Sync { get; init; }

        public required FakeUrlLauncher Launcher { get; init; }

        public required InventoryCardViewModel Inventory { get; init; }
    }

    private static HardFarmList SmallList() => new(
        [new HardFarmGroup("Nation", ["blade-one", "cape-one", "void-highlord-class"]), new HardFarmGroup("Legion", ["blade-two", "pet-one"])],
        "Created by Tester, Revised by Checker");

    private static WikiIndex[] SmallIndexes() =>
    [
        new(WikiUrls.WeaponsSlug, WikiType.Weapon, [new WikiIndexEntry("blade-one", "Blade One"), new WikiIndexEntry("blade-two", "Blade Two")]),
        new(WikiUrls.CapesSlug, WikiType.Cape, [new WikiIndexEntry("cape-one", "Cape One")]),
        new(WikiUrls.PetsSlug, WikiType.Pet, [new WikiIndexEntry("pet-one", "Pet One")]),
    ];

    private static async Task<Host> Build(HardFarmList? list = null, WikiIndex[]? indexes = null, IReadOnlyList<string>? pending = null)
    {
        var store = new HardFarmStore(new InMemoryStoredDataStore().Module());
        if (list is not null)
        {
            store.SaveList(list, At);
            foreach (var index in indexes ?? [])
            {
                store.SaveIndex(index, At);
            }
        }

        var sync = new FakeHardFarmSync { Pending = pending ?? [] };
        var launcher = new FakeUrlLauncher();
        var inventory = new InventoryCardViewModel(new SidebarViewModel());
        var vm = new HardFarmsViewModel(store, sync, inventory, launcher);
        await vm.RestoreAsync();
        var view = new HardFarmsView { DataContext = vm };
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        Flush();
        return new Host { Vm = vm, View = view, Window = window, Sync = sync, Launcher = launcher, Inventory = inventory };
    }

    // Pumps layout, render ticks and queued jobs until the condition holds; never a fixed number of passes.
    private static void SettleUntil(Window window, Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Flush();
            if (condition())
            {
                return;
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Timed out waiting for: {what}.");
            Thread.Sleep(5);
        }
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private static int Realized(ListBox list) => list.GetVisualDescendants().OfType<ListBoxItem>().Count();

    private static IEnumerable<string?> NamesShown(ListBox list) =>
        list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "HardFarmsRowName").Select(t => t.Text);

    private static void WaitForRows(Host h, int count) =>
        SettleUntil(h.Window, () => h.View.Named<ListBox>("HardFarmsList").ItemCount == count
            && Realized(h.View.Named<ListBox>("HardFarmsList")) == count, $"{count} rows to be realized");

    [AvaloniaFact]
    public void The_tab_is_called_Hard_Farms_and_the_Tracker_placeholder_is_gone()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        var tabs = window.FindControl<TabControl>("Tabs")!;
        var headers = tabs.Items.OfType<TabItem>().Select(t => t.Header).ToList();
        Assert.Contains("Hard Farms", headers);
        Assert.DoesNotContain("Tracker", headers);
        Assert.Equal(5, headers.Count);
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, "Hard Farms"));
        SettleUntil(window, () => window.GetVisualDescendants().OfType<HardFarmsView>().Any(), "the Hard Farms tab to be created");
        var view = window.GetVisualDescendants().OfType<HardFarmsView>().SingleOrDefault();
        Assert.NotNull(view);
        Assert.Same(services.GetRequiredService<MainViewModel>().HardFarms, view.DataContext);
    }

    [AvaloniaFact]
    public async Task Before_any_import_the_empty_state_is_shown_and_there_is_no_list()
    {
        var h = await Build();

        var empty = h.View.Named<TextBlock>("HardFarmsEmptyState");
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(HardFarmsViewModel.EmptyText, empty.Text);
        Assert.Contains("few seconds", empty.Text);
        Assert.Contains("minute or two", empty.Text);
        Assert.Equal("Update from the Wiki", h.View.Named<Button>("HardFarmsUpdateButton").Content);
        Assert.False(h.View.Named<TextBlock>("HardFarmsPendingLine").IsEffectivelyVisible);
        Assert.False(h.View.Named<ListBox>("HardFarmsList").IsEffectivelyVisible);
        Assert.True(h.View.Named<Button>("HardFarmsUpdateButton").IsEffectivelyEnabled);
        Assert.False(h.View.Named<Button>("HardFarmsCancelButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task Controls_have_accessible_names_and_the_search_box_a_watermark()
    {
        var h = await Build(SmallList(), SmallIndexes());

        foreach (var name in new[] { "HardFarmsUpdateButton", "HardFarmsCancelButton", "HardFarmsTypeFilter", "HardFarmsGroupFilter",
                     "HardFarmsOwnedFilter", "HardFarmsSearchBox", "HardFarmsList", "HardFarmsPendingLine" })
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(h.View.Named<Control>(name))), name);
        }

        Assert.False(string.IsNullOrEmpty(h.View.Named<TextBox>("HardFarmsSearchBox").Watermark));
    }

    [AvaloniaFact]
    public async Task A_stored_list_is_shown_with_names_types_groups_badges_the_summary_and_the_credit()
    {
        var h = await Build(SmallList(), SmallIndexes());
        WaitForRows(h, 5);

        var list = h.View.Named<ListBox>("HardFarmsList");
        Assert.False(h.View.Named<TextBlock>("HardFarmsEmptyState").IsEffectivelyVisible);
        Assert.Equal(["Blade One", "Cape One", "Void Highlord Class", "Blade Two", "Pet One"], NamesShown(list));
        var texts = list.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Weapon", texts);
        Assert.Contains("Legion", texts);
        Assert.Equal(5, list.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "HardFarmsRowBadge" && t.Text == "Unknown"));
        Assert.Equal(5, list.GetVisualDescendants().OfType<Button>().Count(b => b.Name == "HardFarmsOpenButton"));
        Assert.Equal(h.Vm.SummaryText, h.View.Named<TextBlock>("HardFarmsSummaryLine").Text);
        Assert.Equal(h.Vm.CreditText, h.View.Named<TextBlock>("HardFarmsCreditLine").Text);
        Assert.Contains("Tester", h.View.Named<TextBlock>("HardFarmsCreditLine").Text);
        Assert.Contains("CC BY-SA 3.0", h.View.Named<TextBlock>("HardFarmsCreditLine").Text);
        Assert.StartsWith("Last updated", h.View.Named<TextBlock>("HardFarmsLastUpdatedLine").Text);
    }

    [AvaloniaFact]
    public async Task Badges_say_Owned_or_Missing_in_words_when_an_inventory_is_known()
    {
        var h = await Build(SmallList(), SmallIndexes());
        h.Inventory.Data = new CharacterInventory([new InventoryItem("Blade One", "Sword", 1, false, false)]);
        WaitForRows(h, 5);

        var badges = h.View.Named<ListBox>("HardFarmsList").GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Name == "HardFarmsRowBadge").Select(t => t.Text).ToList();
        Assert.Equal(1, badges.Count(t => t == "Owned"));
        Assert.Equal(4, badges.Count(t => t == "Missing"));
        Assert.Equal(0, badges.Count(t => t == "Unknown"));
        Assert.Equal("1 of 5 owned", h.View.Named<TextBlock>("HardFarmsSummaryLine").Text);
    }

    [AvaloniaFact]
    public async Task The_type_group_and_owned_filters_and_the_search_narrow_the_list_and_update_the_summary()
    {
        var h = await Build(SmallList(), SmallIndexes());
        WaitForRows(h, 5);
        var list = h.View.Named<ListBox>("HardFarmsList");
        var summary = h.View.Named<TextBlock>("HardFarmsSummaryLine");

        var typeBox = h.View.Named<ComboBox>("HardFarmsTypeFilter");
        Assert.Equal(9, typeBox.ItemCount);
        typeBox.SelectedItem = h.Vm.TypeOptions.Single(o => o.Value == WikiType.Weapon);
        Flush();
        WaitForRows(h, 2);
        Assert.Equal(["Blade One", "Blade Two"], NamesShown(list));
        var afterType = summary.Text;

        var groupBox = h.View.Named<ComboBox>("HardFarmsGroupFilter");
        Assert.Equal(3, groupBox.ItemCount);
        groupBox.SelectedItem = h.Vm.GroupOptions.Single(o => o.Name == "Legion");
        Flush();
        WaitForRows(h, 1);
        Assert.Equal(["Blade Two"], NamesShown(list));
        Assert.NotEqual(afterType, summary.Text);
        Assert.Equal(h.Vm.SummaryText, summary.Text);

        typeBox.SelectedItem = HardFarmsViewModel.AllTypes;
        groupBox.SelectedItem = HardFarmsViewModel.AllGroups;
        Flush();
        WaitForRows(h, 5);

        var ownedBox = h.View.Named<ComboBox>("HardFarmsOwnedFilter");
        Assert.Equal(4, ownedBox.ItemCount);
        ownedBox.SelectedItem = h.Vm.OwnedOptions.Single(o => o.Value == Ownership.Owned);
        Flush();
        SettleUntil(h.Window, () => list.ItemCount == 0, "no owned rows");
        Assert.True(h.View.Named<TextBlock>("HardFarmsNoMatchLine").IsEffectivelyVisible);

        ownedBox.SelectedItem = HardFarmsViewModel.AllOwnership;
        var search = h.View.Named<TextBox>("HardFarmsSearchBox");
        search.Text = "cape";
        Flush();
        WaitForRows(h, 1);
        Assert.Equal(["Cape One"], NamesShown(list));
        Assert.Equal("cape", h.Vm.SearchText);
        Assert.False(h.View.Named<TextBlock>("HardFarmsNoMatchLine").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task While_updating_Update_is_disabled_Cancel_is_enabled_and_the_progress_is_shown()
    {
        var h = await Build();
        h.Sync.Gate = new TaskCompletionSource();
        h.Sync.Result = new SyncResult(SyncStatus.Failed, "stopped", []);

        var running = h.Vm.UpdateCommand.ExecuteAsync(null);
        SettleUntil(h.Window, () => h.Sync.Progress is not null, "the sync to start");
        h.Sync.Progress!(new SyncProgress(5, 15, "Index 6 of 15 · /pets", "Waiting 10 s before trying /pets again"));
        SettleUntil(h.Window, () => h.View.Named<TextBlock>("HardFarmsProgressLine").Text
            == "Index 6 of 15 · /pets · Waiting 10 s before trying /pets again", "the progress text");

        Assert.False(h.View.Named<Button>("HardFarmsUpdateButton").IsEffectivelyEnabled);
        Assert.True(h.View.Named<Button>("HardFarmsCancelButton").IsEffectivelyEnabled);
        Assert.False(h.View.Named<TextBlock>("HardFarmsEmptyState").IsEffectivelyVisible);
        Assert.False(h.View.Named<TextBlock>("HardFarmsPendingLine").IsEffectivelyVisible);

        h.Sync.Gate.SetResult();
        await running;
        SettleUntil(h.Window, () => h.View.Named<Button>("HardFarmsUpdateButton").IsEffectivelyEnabled, "update to be enabled again");
        Assert.False(h.View.Named<Button>("HardFarmsCancelButton").IsEffectivelyEnabled);
        SettleUntil(h.Window, () => h.View.Named<TextBlock>("HardFarmsStatusLine").Text == "stopped", "the final message");
    }

    [AvaloniaFact]
    public async Task Rows_appear_while_the_pass_runs_and_a_partial_pass_leaves_Continue_with_the_pending_line()
    {
        var h = await Build();
        h.Sync.Gate = new TaskCompletionSource();
        h.Sync.Result = new SyncResult(SyncStatus.Partial, "Stored 5 entries in the list and 12 index pages. 3 pages pending.", ["pets", "houses", "grounds"]);
        var button = h.View.Named<Button>("HardFarmsUpdateButton");
        Assert.Equal("Update from the Wiki", button.Content);

        var running = h.Vm.UpdateCommand.ExecuteAsync(null);
        SettleUntil(h.Window, () => h.Sync.Saved is not null, "the sync to start");
        h.Sync.Saved!(new SyncUpdate(SmallList(), At, WikiTypeTable.Empty, null));
        WaitForRows(h, 5);
        Assert.Equal(["Blade One", "Cape One", "Void Highlord Class", "Blade Two", "Pet One"], NamesShown(h.View.Named<ListBox>("HardFarmsList")));

        h.Vm.TypeFilter = h.Vm.TypeOptions.Single(o => o.Value == WikiType.Weapon);
        WaitForRows(h, 0);
        h.Sync.Saved!(new SyncUpdate(null, null, WikiTypeTable.From(SmallIndexes()), At));
        WaitForRows(h, 2);
        Assert.Equal(["Blade One", "Blade Two"], NamesShown(h.View.Named<ListBox>("HardFarmsList")));

        h.Sync.Gate.SetResult();
        await running;
        var pending = h.View.Named<TextBlock>("HardFarmsPendingLine");
        SettleUntil(h.Window, () => pending.IsEffectivelyVisible, "the pending line");
        Assert.Equal("3 pages pending — Continue", pending.Text);
        Assert.Equal("Continue", button.Content);
        Assert.Equal("Continue reading the pending Wiki pages", AutomationProperties.GetName(button));
        Assert.Equal("3 pages pending — Continue", AutomationProperties.GetName(pending));
        Assert.True(button.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task A_restored_list_with_nothing_pending_says_Update_from_the_Wiki_and_hides_the_pending_line()
    {
        var h = await Build(SmallList(), SmallIndexes());
        WaitForRows(h, 5);

        Assert.Equal("Update from the Wiki", h.View.Named<Button>("HardFarmsUpdateButton").Content);
        Assert.False(h.View.Named<TextBlock>("HardFarmsPendingLine").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_restored_list_with_pending_pages_says_Continue_with_the_pending_line()
    {
        var h = await Build(SmallList(), SmallIndexes(), ["pets", "houses"]);
        WaitForRows(h, 5);

        var pending = h.View.Named<TextBlock>("HardFarmsPendingLine");
        SettleUntil(h.Window, () => pending.IsEffectivelyVisible, "the pending line");
        Assert.Equal("2 pages pending — Continue", pending.Text);
        Assert.Equal("Continue", h.View.Named<Button>("HardFarmsUpdateButton").Content);
    }

    [AvaloniaFact]
    public async Task Pressing_Continue_runs_the_sync_again()
    {
        var h = await Build(SmallList(), SmallIndexes(), ["pets"]);
        h.Sync.Result = new SyncResult(SyncStatus.Updated, "Stored.", []);
        var button = h.View.Named<Button>("HardFarmsUpdateButton");
        SettleUntil(h.Window, () => Equals(button.Content, "Continue"), "the Continue button");

        button.Command!.Execute(null);

        SettleUntil(h.Window, () => h.Sync.Calls == 1, "the sync to be called");
        SettleUntil(h.Window, () => Equals(button.Content, "Update from the Wiki"), "the button to go back to Update");
        Assert.False(h.View.Named<TextBlock>("HardFarmsPendingLine").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Open_on_the_Wiki_passes_the_row_to_the_launcher()
    {
        var h = await Build(SmallList(), SmallIndexes());
        WaitForRows(h, 5);

        var button = h.View.Named<ListBox>("HardFarmsList").GetVisualDescendants().OfType<Button>()
            .First(b => b.Name == "HardFarmsOpenButton");
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
        var row = Assert.IsType<HardFarmRow>(button.CommandParameter);
        Assert.True(button.Command!.CanExecute(row));
        button.Command.Execute(row);

        Assert.Equal([row.WikiUrl], h.Launcher.Opened);
    }

    [AvaloniaFact]
    public async Task A_three_thousand_row_list_stays_virtualized()
    {
        const int count = 3000;
        var slugs = Enumerable.Range(1, count).Select(i => $"item-{i}").ToList();
        var list = new HardFarmList([new HardFarmGroup("Big", slugs)], "Created by Tester");
        var index = new WikiIndex(WikiUrls.MiscItemsSlug, WikiType.Other, slugs.Select(s => new WikiIndexEntry(s, s.Replace('-', ' '))).ToList());
        var h = await Build(list, [index]);

        var box = h.View.Named<ListBox>("HardFarmsList");
        SettleUntil(h.Window, () => Realized(box) >= 5, "the first rows to be realized");
        Assert.Equal(count, box.ItemCount);
        Assert.InRange(Realized(box), 5, 200);

        h.Vm.SearchText = "item 29";
        SettleUntil(h.Window, () => box.ItemCount < count, "the search to narrow the list");
        SettleUntil(h.Window, () => Realized(box) >= 1, "filtered rows to be realized");
        Assert.InRange(Realized(box), 1, 200);
    }
}
