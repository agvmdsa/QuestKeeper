using System.Diagnostics;
using AqwCompanion.App.Account;
using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Tests.TestDoubles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AqwCompanion.Tests.Inventory;

public class InventoryViewTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private static CharacterInventory Make(int count) =>
        new([.. Enumerable.Range(1, count).Select(i =>
            new InventoryItem($"Test Item {i}", i % 3 == 0 ? "Armor" : "Weapon", i % 5 == 0 ? 7 : 1, false, false))]);

    private static (AccountViewModel Vm, AccountView View, Window Window) Host(bool connected, CharacterInventory? inventory = null)
    {
        var persistence = new FakeAccountDataPersistence();
        if (connected)
        {
            persistence.ToRestore = new RestoredAccount("Test Hero", SampleAccount.Summary, At, null, null, null, null,
                Inventory: inventory ?? Make(12), InventoryReadAtUtc: At);
        }

        var vm = AccountViewModelBuilder.Create(new FakeSignInDialog(), persistence: persistence);
        var view = new AccountView { DataContext = vm };
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        return (vm, view, window);
    }

    // Pumps layout, render ticks and queued jobs until the condition holds. Polls the condition rather than
    // a fixed number of passes: the sidebar slides in and the virtualizing panel realizes rows over a
    // machine-dependent number of passes. The bound only exists to turn a hang into a failure.
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

    // The sidebar content is templated lazily, so the list may not exist until the first layout passes.
    private static ListBox WaitForList(Window window, AccountView view)
    {
        SettleUntil(window, () => view.GetVisualDescendants().OfType<ListBox>().Any(l => l.Name == "InventoryItemList"),
            "the inventory list to be created");
        return view.Named<ListBox>("InventoryItemList");
    }

    private static int Realized(ListBox list) => list.GetVisualDescendants().OfType<ListBoxItem>().Count();

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private static void Click(Window window, Control target, Point? offset = null)
    {
        var p = target.TranslatePoint(offset ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Flush();
    }

    private static async Task<(AccountViewModel Vm, AccountView View, Window Window)> Connected(CharacterInventory? inventory = null)
    {
        var host = Host(connected: true, inventory);
        await host.Vm.RestoreStoredDataAsync();
        host.Window.UpdateLayout();
        Flush();
        return host;
    }

    [AvaloniaFact]
    public async Task The_card_is_hidden_until_connected_and_shows_the_summary_after()
    {
        var (vm, view, window) = Host(connected: true);
        var card = view.FindControl<InventoryCardView>("InventoryCard")!;
        Assert.False(card.IsVisible);

        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();

        Assert.True(card.IsVisible);
        Assert.True(view.Named<Border>("InventoryPanel").IsEffectivelyVisible);
        Assert.Equal("12 items", view.Named<TextBlock>("InventoryCountLine").Text);
        Assert.Equal(2, view.Named<ItemsControl>("InventoryTypePreviewList").ItemCount);
        Assert.False(view.Named<TextBlock>("InventoryStatusLine").IsVisible);
        Assert.True(view.Named<TextBlock>("InventoryUpdatedLine").IsVisible);
    }

    [AvaloniaFact]
    public void The_card_stays_hidden_when_not_connected()
    {
        var (_, view, window) = Host(connected: false);
        window.UpdateLayout();
        Flush();

        Assert.False(view.FindControl<InventoryCardView>("InventoryCard")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task View_all_and_tapping_the_card_open_the_inventory_sidebar()
    {
        var (vm, view, window) = await Connected();

        Click(window, view.Named<Button>("ViewAllInventoryButton"));
        Assert.True(vm.Sidebar.IsOpen);
        Assert.Same(vm.Inventory, vm.Sidebar.Content);
        Assert.Equal("Inventory", view.Named<TextBlock>("SidebarTitleText").Text);

        vm.Sidebar.CloseCommand.Execute(null);
        Flush();
        Assert.False(vm.Sidebar.IsOpen);

        Click(window, view.Named<Border>("InventoryPanel"), new Point(20, 20));
        Assert.True(vm.Sidebar.IsOpen);
        Assert.Equal("Inventory", view.Named<TextBlock>("SidebarTitleText").Text);
        Assert.Equal(12, view.Named<ListBox>("InventoryItemList").ItemCount);
    }

    [AvaloniaFact]
    public async Task Tapping_the_card_without_items_does_not_open_the_sidebar()
    {
        var (vm, view, window) = await Connected(new CharacterInventory([]));

        Click(window, view.Named<Border>("InventoryPanel"), new Point(20, 20));

        Assert.False(vm.Sidebar.IsOpen);
    }

    [AvaloniaFact]
    public async Task Typing_in_the_filter_box_narrows_the_list_and_shows_the_no_match_text()
    {
        var (vm, view, window) = await Connected();
        vm.Inventory.OpenDetailsCommand.Execute(null);
        Flush();
        window.UpdateLayout();

        var box = view.Named<TextBox>("InventoryFilterBox");
        Assert.Equal("Filter by name", box.Watermark);
        Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(box)));

        box.Text = "item 11";
        Flush();
        Assert.Equal("item 11", vm.Inventory.FilterText);
        Assert.Equal(1, view.Named<ListBox>("InventoryItemList").ItemCount);
        Assert.Equal("1 of 12 items", view.Named<TextBlock>("InventoryTotalLine").Text);
        Assert.False(view.Named<TextBlock>("InventoryNoMatchLine").IsVisible);

        box.Text = "zzz";
        Flush();
        Assert.Equal(0, view.Named<ListBox>("InventoryItemList").ItemCount);
        Assert.True(view.Named<TextBlock>("InventoryNoMatchLine").IsVisible);

        box.Text = "";
        Flush();
        Assert.Equal(12, view.Named<ListBox>("InventoryItemList").ItemCount);
        Assert.False(view.Named<TextBlock>("InventoryNoMatchLine").IsVisible);
    }

    [AvaloniaFact]
    public async Task Rows_show_the_count_only_above_one_and_the_type()
    {
        var (vm, view, window) = await Connected(new CharacterInventory(
        [
            new InventoryItem("Test Single", "Weapon", 1, false, false),
            new InventoryItem("Test Stack", "Item", 25, false, false),
        ]));
        vm.Inventory.OpenDetailsCommand.Execute(null);
        Flush();

        var list = WaitForList(window, view);
        SettleUntil(window, () => Realized(list) == 2
            && list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Weapon")
            && list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "×25" && t.IsEffectivelyVisible),
            "both rows to be realized with their text");
        var counts = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "InventoryRowCount").ToList();
        Assert.Equal("×25", Assert.Single(counts, t => t.IsEffectivelyVisible).Text);
        Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => t.Text == "×1");
        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Weapon");
    }

    [AvaloniaFact]
    public async Task A_very_long_name_wraps_and_the_count_stays_visible()
    {
        var longName = string.Join(' ', Enumerable.Repeat("Silverish", 22))[..200];
        var (vm, view, window) = await Connected(new CharacterInventory([new InventoryItem(longName, "Sword", 25, false, false)]));
        vm.Inventory.OpenDetailsCommand.Execute(null);
        Flush();

        var list = WaitForList(window, view);
        SettleUntil(window, () => list.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Name == "InventoryRowCount" && t.IsEffectivelyVisible && t.Bounds.Width > 0),
            "the row count to be realized");
        var count = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "InventoryRowCount");
        var name = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == longName);
        var item = list.GetVisualDescendants().OfType<ListBoxItem>().Single();

        Assert.Equal("×25", count.Text);
        Assert.True(name.Bounds.Height > name.FontSize * 1.8, "the long name should wrap onto several lines");
        var right = count.TranslatePoint(new Point(count.Bounds.Width, 0), list)!.Value.X;
        Assert.True(right <= list.Bounds.Width, $"the count ends at {right}, past the list width {list.Bounds.Width}");
        Assert.True(item.Bounds.Width <= list.Bounds.Width);
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_sidebar_while_the_filter_box_has_focus()
    {
        var (vm, view, window) = await Connected();
        vm.Inventory.OpenDetailsCommand.Execute(null);
        Flush();
        window.UpdateLayout();
        view.Named<TextBox>("InventoryFilterBox").Focus();
        Flush();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Flush();

        Assert.False(vm.Sidebar.IsOpen);
    }

    [AvaloniaFact]
    public async Task A_five_thousand_item_inventory_opens_quickly_and_only_realizes_the_visible_rows()
    {
        var (vm, view, window) = await Connected(Make(5000));

        vm.Inventory.OpenDetailsCommand.Execute(null);

        // Virtualization is structural: once settled, a small window of rows is realized, never all 5000.
        // (Opening within the generous bound of SettleUntil also guards against a hang.)
        var list = WaitForList(window, view);
        SettleUntil(window, () => Realized(list) >= 5, "the first rows to be realized");
        Assert.Equal(5000, list.ItemCount);
        Assert.InRange(Realized(list), 5, 200);

        vm.Inventory.FilterText = "Item 49";
        SettleUntil(window, () => list.ItemCount < 5000, "the filter to narrow the list");
        SettleUntil(window, () => Realized(list) >= 1, "filtered rows to be realized");
        Assert.InRange(Realized(list), 1, 200);
    }
}
