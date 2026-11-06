using AqwCompanion.App.Account;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Tests.TestDoubles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AqwCompanion.Tests.Account;

public class AccountViewTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private static readonly CharacterEquipment Gear =
        new("Test Hero", 10, null, null, null, null, null,
            [.. Enumerable.Range(1, 7).Select(i => new EquipmentSlot($"Slot {i}", $"Item {i}", null, false)),
                new EquipmentSlot("Cape", "Custom Cape", null, true)],
            [], 3);

    private static readonly CharacterBadges SomeBadges =
        new([new Badge("Test Badge 1", "Description 1", "Quests"), new Badge("Test Badge 2", "Description 2", "Events")]);

    private static (AccountViewModel Vm, AccountView View, Window Window) Host(bool connected)
    {
        var persistence = new FakeAccountDataPersistence();
        if (connected)
        {
            persistence.ToRestore = new RestoredAccount("Test Hero", SampleAccount.Summary, At, Gear, At, SomeBadges, At);
        }

        var vm = AccountViewModelBuilder.Create(new FakeSignInDialog(), persistence: persistence);
        var view = new AccountView { DataContext = vm };
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        return (vm, view, window);
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();


    private static void Click(Window window, Control target, Point? offset = null)
    {
        var p = target.TranslatePoint(offset ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Flush();
    }

    [AvaloniaFact]
    public async Task The_three_cards_are_hidden_until_connected_and_visible_after()
    {
        var (vm, view, window) = Host(connected: true);

        // The hosted cards do not build their content while hidden; AccountView sets their visibility.
        Assert.False(view.FindControl<SummaryCardView>("SummaryCard")!.IsVisible);
        Assert.False(view.FindControl<EquipmentCardView>("EquipmentCard")!.IsVisible);
        Assert.False(view.FindControl<BadgesCardView>("BadgesCard")!.IsVisible);
        Assert.True(view.Named<Border>("AccountCard").IsVisible);

        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();

        Assert.True(view.FindControl<SummaryCardView>("SummaryCard")!.IsVisible);
        Assert.True(view.FindControl<EquipmentCardView>("EquipmentCard")!.IsVisible);
        Assert.True(view.FindControl<BadgesCardView>("BadgesCard")!.IsVisible);
        Assert.True(view.Named<Border>("SummaryPanel").IsEffectivelyVisible);
        Assert.True(view.Named<Border>("EquippedPanel").IsEffectivelyVisible);
        Assert.True(view.Named<Border>("BadgesPanel").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Tapping_the_equipped_card_opens_the_sidebar_with_the_slot_list()
    {
        var (vm, view, window) = Host(connected: true);
        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();

        Click(window, view.Named<Border>("EquippedPanel"), new Point(20, 20));

        Assert.True(vm.Sidebar.IsOpen);
        Assert.Equal("Equipped", view.Named<TextBlock>("SidebarTitleText").Text);
        Assert.Equal("Test Hero", view.Named<TextBlock>("EquipmentTitle").Text);
        Assert.Equal(7, view.Named<ItemsControl>("StandardSlotList").ItemCount);
        Assert.Equal(1, view.Named<ItemsControl>("CustomSlotList").ItemCount);
    }

    [AvaloniaFact]
    public async Task View_all_opens_the_equipped_sidebar()
    {
        var (vm, view, window) = Host(connected: true);
        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();

        Click(window, view.Named<Button>("ViewAllButton"));

        Assert.True(vm.Sidebar.IsOpen);
        Assert.Same(vm.Equipment, vm.Sidebar.Content);
        Assert.Equal("Equipped", view.Named<TextBlock>("SidebarTitleText").Text);
        Assert.Equal("Equipped details", vm.Sidebar.AutomationName);
    }

    [AvaloniaFact]
    public async Task Tapping_the_badges_card_and_view_all_open_the_badges_sidebar_with_groups()
    {
        var (vm, view, window) = Host(connected: true);
        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();

        Click(window, view.Named<Border>("BadgesPanel"), new Point(20, 20));

        Assert.True(vm.Sidebar.IsOpen);
        Assert.Equal("Badges", view.Named<TextBlock>("SidebarTitleText").Text);
        Assert.Equal(2, view.Named<ItemsControl>("BadgeGroupList").ItemCount);

        vm.Sidebar.CloseCommand.Execute(null);
        Flush();
        Assert.False(vm.Sidebar.IsOpen);

        Click(window, view.Named<Button>("ViewAllBadgesButton"));
        Assert.True(vm.Sidebar.IsOpen);
        Assert.Same(vm.Badges, vm.Sidebar.Content);
    }

    [AvaloniaFact]
    public async Task Tapping_a_card_without_data_does_not_open_the_sidebar()
    {
        var persistence = new FakeAccountDataPersistence
        {
            ToRestore = new RestoredAccount("Test Hero", null, At, null, null, null, null),
        };
        var vm = AccountViewModelBuilder.Create(new FakeSignInDialog(), persistence: persistence);
        var view = new AccountView { DataContext = vm };
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();

        Click(window, view.Named<Border>("EquippedPanel"), new Point(20, 20));
        Click(window, view.Named<Border>("BadgesPanel"), new Point(20, 20));

        Assert.False(vm.Sidebar.IsOpen);
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_sidebar_and_focus_returns_to_the_opener()
    {
        var (vm, view, window) = Host(connected: true);
        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();
        var opener = view.Named<Button>("ViewAllButton");
        opener.Focus();
        opener.Command!.Execute(null);
        Flush();

        Assert.True(vm.Sidebar.IsOpen);
        Assert.True(view.Named<Border>("Sidebar").IsVisible);
        Assert.Same(view.Named<Button>("SidebarCloseButton"), window.FocusManager!.GetFocusedElement());

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Flush();

        Assert.False(vm.Sidebar.IsOpen);
        Assert.Same(opener, window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task The_close_button_closes_the_sidebar_and_returns_focus()
    {
        var (vm, view, window) = Host(connected: true);
        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();
        var opener = view.Named<Button>("ViewAllBadgesButton");
        opener.Focus();
        opener.Command!.Execute(null);
        Flush();

        // The panel is still sliding in (headless time does not advance), so activate the focused close button by keyboard.
        Assert.Same(view.Named<Button>("SidebarCloseButton"), window.FocusManager!.GetFocusedElement());
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Flush();

        Assert.False(vm.Sidebar.IsOpen);
        Assert.Same(opener, window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task Tapping_the_scrim_closes_the_sidebar()
    {
        var (vm, view, window) = Host(connected: true);
        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();
        Flush();
        view.Named<Button>("ViewAllButton").Command!.Execute(null);
        Flush();
        Assert.True(vm.Sidebar.IsOpen);

        Click(window, view.Named<Border>("Scrim"), new Point(50, 50));

        Assert.False(vm.Sidebar.IsOpen);
    }
}
