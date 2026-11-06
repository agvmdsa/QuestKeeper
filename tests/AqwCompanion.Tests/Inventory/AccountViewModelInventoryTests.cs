using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Inventory;

/// <summary>How the Account tab loads, stores, restores and forgets the inventory.</summary>
public class AccountViewModelInventoryTests
{
    private static readonly DateTimeOffset Now = new(2030, 3, 4, 5, 6, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly FakeSignInDialog _dialog = new();
    private readonly FakeAccountDataPersistence _persistence = new();
    private readonly FakeAccountSessionData _session = new();

    private static Func<SignInMode, Task<SignInResult>> Reports(SignInResult result) => _ => Task.FromResult(result);

    private static CharacterEquipment Equipment(string name, int? characterId) =>
        new(name, 10, null, null, null, null, null, [new EquipmentSlot("Armor", "Test Armor", null, false)], [], characterId);

    private static CharacterBadges Badges(int count) =>
        new(Enumerable.Range(1, count).Select(i => new Badge($"Test Badge {i}", "d", "Quests")).ToList());

    private static CharacterInventory Items(int count) =>
        new(Enumerable.Range(1, count).Select(i => new InventoryItem($"Test Item {i}", "Item", i, false, false)).ToList());

    private static Func<string, Task<EquipmentLoadResult>> Equips(CharacterEquipment equipment, bool persisted = false) =>
        _ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Loaded, equipment, persisted));

    private static Func<int, Task<BadgesLoadResult>> HasBadges() =>
        _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Loaded, Badges(2)));

    private static Func<int, Task<InventoryLoadResult>> HasInventory(CharacterInventory inventory, bool persisted = false) =>
        _ => Task.FromResult(new InventoryLoadResult(InventoryLoadStatus.Loaded, inventory, persisted));

    private static Func<int, Task<InventoryLoadResult>> Status(InventoryLoadStatus status) =>
        _ => Task.FromResult(new InventoryLoadResult(status));

    private AccountViewModel Build(
        Func<string, Task<EquipmentLoadResult>> equipment,
        Func<int, Task<InventoryLoadResult>>? inventory,
        Func<int, Task<BadgesLoadResult>>? badges = null,
        bool persistence = true) =>
        AccountViewModelBuilder.Create(
            _dialog,
            new FakeEquipmentLoader(equipment),
            new FakeBadgesLoader(badges ?? HasBadges()),
            persistence ? _persistence : null,
            new FixedTimeProvider(Now),
            persistence ? _session : null,
            inventory is null ? null : new FakeInventoryLoader(inventory));

    private async Task<AccountViewModel> ConnectedAsync(
        Func<string, Task<EquipmentLoadResult>> equipment, Func<int, Task<InventoryLoadResult>>? inventory, bool persistence = true)
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build(equipment, inventory, persistence: persistence);
        await vm.SignInCommand.ExecuteAsync(null);
        return vm;
    }

    private static string ExpectedUpdated(DateTimeOffset at) =>
        "Last updated " + at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static RestoredAccount Stored(bool withEquipment = true) =>
        new(
            "Test Hero", SampleAccount.Summary, Earlier,
            withEquipment ? Equipment("Test Hero", 3) : null, withEquipment ? Earlier.AddMinutes(1) : null,
            Badges(2), Earlier.AddMinutes(2),
            Items(4), Earlier.AddMinutes(3));

    [Fact]
    public async Task Loads_the_inventory_for_the_character_id_after_the_equipment_and_the_badges()
    {
        var order = new List<string>();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var inventoryLoader = new FakeInventoryLoader(id =>
        {
            order.Add("inventory");
            return Task.FromResult(new InventoryLoadResult(InventoryLoadStatus.Loaded, Items(7)));
        });
        var vm = AccountViewModelBuilder.Create(
            _dialog,
            new FakeEquipmentLoader(name =>
            {
                order.Add("equipment");
                return Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Loaded, Equipment(name, 3)));
            }),
            new FakeBadgesLoader(id =>
            {
                order.Add("badges");
                return Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Loaded, Badges(2)));
            }),
            inventoryLoader: inventoryLoader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal(["equipment", "badges", "inventory"], order);
        Assert.Equal([3], inventoryLoader.Requested);
        Assert.True(vm.Inventory.HasData);
        Assert.Equal("7 items", vm.Inventory.CountText);
        Assert.Equal(string.Empty, vm.Inventory.Status);
        Assert.True(vm.Inventory.OpenDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Shows_the_loading_text_while_the_inventory_is_read()
    {
        var gate = new TaskCompletionSource<InventoryLoadResult>();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build(Equips(Equipment("Test Hero", 3)), _ => gate.Task, persistence: false);

        var signingIn = vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal(InventoryCardViewModel.LoadingText, vm.Inventory.Status);
        Assert.False(vm.Inventory.HasData);
        gate.SetResult(new InventoryLoadResult(InventoryLoadStatus.Loaded, Items(2)));
        await signingIn;
        Assert.Equal(string.Empty, vm.Inventory.Status);
        Assert.True(vm.Inventory.HasData);
    }

    [Fact]
    public async Task An_empty_list_shows_the_none_found_text_and_cannot_open_the_sidebar()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasInventory(Items(0)));

        Assert.False(vm.Inventory.HasData);
        Assert.Equal(InventoryCardViewModel.NotFoundText, vm.Inventory.Status);
        Assert.False(vm.Inventory.OpenDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_unreadable_inventory_shows_the_none_found_text()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), Status(InventoryLoadStatus.NotFound));

        Assert.False(vm.Inventory.HasData);
        Assert.Equal(InventoryCardViewModel.NotFoundText, vm.Inventory.Status);
    }

    [Fact]
    public async Task An_inventory_that_cannot_be_fetched_shows_the_unavailable_text()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), Status(InventoryLoadStatus.Unavailable));

        Assert.Equal(InventoryCardViewModel.UnavailableText, vm.Inventory.Status);
    }

    [Fact]
    public async Task Without_a_character_id_the_inventory_is_not_requested()
    {
        var loader = new FakeInventoryLoader(HasInventory(Items(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(
            _dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", null))), new FakeBadgesLoader(HasBadges()), inventoryLoader: loader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Empty(loader.Requested);
        Assert.Equal(InventoryCardViewModel.UnavailableText, vm.Inventory.Status);
    }

    [Fact]
    public async Task When_the_equipment_is_not_loaded_the_inventory_is_not_requested()
    {
        var loader = new FakeInventoryLoader(HasInventory(Items(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(
            _dialog,
            new FakeEquipmentLoader(_ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Unavailable))),
            inventoryLoader: loader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Empty(loader.Requested);
        Assert.Equal(InventoryCardViewModel.UnavailableText, vm.Inventory.Status);
    }

    [Fact]
    public async Task Without_an_inventory_loader_the_card_stays_empty()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), null, persistence: false);

        Assert.True(vm.Equipment.HasData);
        Assert.False(vm.Inventory.HasData);
        Assert.Equal(string.Empty, vm.Inventory.Status);
    }

    [Fact]
    public async Task Persisted_inventory_with_items_is_saved_with_its_read_time()
    {
        var inventory = Items(3);
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3), true), HasInventory(inventory, true));

        Assert.Equal((inventory, Now), _persistence.SavedInventory.Single());
        Assert.Equal(ExpectedUpdated(Now), vm.Inventory.UpdatedText);
    }

    [Fact]
    public async Task Loaded_but_not_persisted_inventory_is_shown_but_not_saved()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3), true), HasInventory(Items(3), false));

        Assert.True(vm.Inventory.HasData);
        Assert.Empty(_persistence.SavedInventory);
        Assert.DoesNotContain("RemoveInventory", _persistence.Calls);
        Assert.Equal(string.Empty, vm.Inventory.UpdatedText);
    }

    [Fact]
    public async Task Persisted_empty_inventory_removes_the_stored_inventory()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3), true), HasInventory(Items(0), true));

        Assert.Contains("RemoveInventory", _persistence.Calls);
        Assert.Empty(_persistence.SavedInventory);
        Assert.Equal(string.Empty, vm.Inventory.UpdatedText);
    }

    [Fact]
    public async Task Ok_but_not_persisted_empty_inventory_does_not_remove_the_stored_inventory()
    {
        await ConnectedAsync(Equips(Equipment("Test Hero", 3), true), HasInventory(Items(0), false));

        Assert.DoesNotContain("RemoveInventory", _persistence.Calls);
    }

    [Fact]
    public async Task Inventory_not_found_never_removes_the_stored_inventory()
    {
        _persistence.ToRestore = Stored();
        var vm = Build(Equips(Equipment("Test Hero", 3), true), Status(InventoryLoadStatus.NotFound));
        await vm.RestoreStoredDataAsync();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain("RemoveInventory", _persistence.Calls);
        Assert.Null(vm.Inventory.Data);
        Assert.Equal(InventoryCardViewModel.NotFoundText, vm.Inventory.Status);
    }

    [Fact]
    public async Task Unavailable_inventory_keeps_the_restored_inventory_untouched()
    {
        _persistence.ToRestore = Stored();
        var vm = Build(Equips(Equipment("Test Hero", 3), true), Status(InventoryLoadStatus.Unavailable));
        await vm.RestoreStoredDataAsync();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain("RemoveInventory", _persistence.Calls);
        Assert.Equal(4, vm.Inventory.Data!.Count);
        Assert.Equal(InventoryCardViewModel.UnavailableText, vm.Inventory.Status);
    }

    [Fact]
    public async Task Restore_fills_the_inventory_with_its_read_time_and_seeds_the_session()
    {
        var stored = Stored();
        _persistence.ToRestore = stored;
        var vm = Build(Equips(Equipment("Test Hero", 3)), null);
        var changed = new List<string?>();
        vm.Inventory.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.RestoreStoredDataAsync();

        Assert.Equal(4, vm.Inventory.Data!.Count);
        Assert.Equal(ExpectedUpdated(Earlier.AddMinutes(3)), vm.Inventory.UpdatedText);
        Assert.Contains(nameof(InventoryCardViewModel.UpdatedText), changed);
        Assert.Same(stored, Assert.Single(_session.Seeded));
    }

    [Fact]
    public async Task Restore_without_a_stored_inventory_leaves_the_card_empty()
    {
        _persistence.ToRestore = Stored() with { Inventory = null, InventoryReadAtUtc = null };
        var vm = Build(Equips(Equipment("Test Hero", 3)), null);

        await vm.RestoreStoredDataAsync();

        Assert.Null(vm.Inventory.Data);
        Assert.Equal(string.Empty, vm.Inventory.UpdatedText);
    }

    [Fact]
    public async Task A_different_character_clears_the_previous_inventory_and_closes_its_sidebar()
    {
        _persistence.ToRestore = Stored();
        var vm = Build(Equips(Equipment("Test Hero", 3)), null);
        await vm.RestoreStoredDataAsync();
        vm.Inventory.OpenDetailsCommand.Execute(null);
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Other Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        // The stored inventory of the previous character is removed by SaveIdentity.
        Assert.Equal(["SaveIdentity:Other Hero"], _persistence.Calls);
        Assert.Null(vm.Inventory.Data);
        Assert.Equal(string.Empty, vm.Inventory.UpdatedText);
        Assert.False(vm.Sidebar.IsOpen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sign_out_clears_the_card_and_closes_the_sidebar(bool signedOut)
    {
        _persistence.ToRestore = Stored();
        var vm = Build(Equips(Equipment("Test Hero", 3)), null);
        await vm.RestoreStoredDataAsync();
        vm.Inventory.OpenDetailsCommand.Execute(null);
        vm.Inventory.FilterText = "item";
        vm.Inventory.Status = "x";
        _dialog.OnShow = Reports(signedOut ? SignInResult.SignedOut() : SignInResult.Failed("down"));

        vm.RequestSignOutCommand.Execute(null);
        await vm.ConfirmSignOutCommand.ExecuteAsync(null);

        Assert.Null(vm.Inventory.Data);
        Assert.Equal(string.Empty, vm.Inventory.UpdatedText);
        Assert.Equal(string.Empty, vm.Inventory.Status);
        Assert.Equal(string.Empty, vm.Inventory.FilterText);
        Assert.False(vm.Sidebar.IsOpen);
        Assert.Contains("Forget", _persistence.Calls);
    }

    [Fact]
    public async Task A_sign_out_that_cannot_remove_the_data_keeps_the_inventory()
    {
        _persistence.ToRestore = Stored();
        _persistence.ForgetResult = false;
        var vm = Build(Equips(Equipment("Test Hero", 3)), null);
        await vm.RestoreStoredDataAsync();
        _dialog.OnShow = Reports(SignInResult.SignedOut());

        vm.RequestSignOutCommand.Execute(null);
        await vm.ConfirmSignOutCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Inventory.Data);
    }

    [Fact]
    public async Task The_newest_load_wins_and_a_superseded_load_does_not_touch_the_inventory()
    {
        var first = new TaskCompletionSource<InventoryLoadResult>();
        var second = new TaskCompletionSource<InventoryLoadResult>();
        var calls = 0;
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build(Equips(Equipment("Test Hero", 3), true), _ => ++calls == 1 ? first.Task : second.Task);

        var older = vm.SignInCommand.ExecuteAsync(null);
        await Task.Yield();
        var newer = vm.RefreshCommand.ExecuteAsync(null);
        second.SetResult(new InventoryLoadResult(InventoryLoadStatus.Loaded, Items(5), true));
        await newer;
        first.SetResult(new InventoryLoadResult(InventoryLoadStatus.Loaded, Items(2), true));
        await older;

        Assert.Equal(5, vm.Inventory.Data!.Count);
        Assert.Equal([5], _persistence.SavedInventory.Select(s => s.Inventory.Count));
    }

    [Fact]
    public async Task Sign_out_cancels_an_in_flight_inventory_load()
    {
        var gate = new TaskCompletionSource<InventoryLoadResult>();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build(Equips(Equipment("Test Hero", 3), true), _ => gate.Task);

        var signingIn = vm.SignInCommand.ExecuteAsync(null);
        await Task.Yield();
        _dialog.OnShow = Reports(SignInResult.SignedOut());
        vm.RequestSignOutCommand.Execute(null);
        await vm.ConfirmSignOutCommand.ExecuteAsync(null);
        gate.SetResult(new InventoryLoadResult(InventoryLoadStatus.Loaded, Items(3), true));
        await signingIn;

        Assert.Null(vm.Inventory.Data);
        Assert.Equal(string.Empty, vm.Inventory.Status);
        Assert.Empty(_persistence.SavedInventory);
    }
}
