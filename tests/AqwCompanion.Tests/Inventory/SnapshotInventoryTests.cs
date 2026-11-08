using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Inventory;

public class SnapshotInventoryTests
{
    private static readonly CharacterProfile Profile = new("Fictional Hero", 60, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, true, 11, 0, 0, 0, 0);

    private static OwnedItem Item(string name, string type = "Item", int quantity = 1, ItemFlags flags = ItemFlags.None) =>
        new(name, type, quantity, 1, 1, flags, 1, null);

    private static CharacterSnapshot Snapshot(IReadOnlyList<OwnedItem> items, IReadOnlyList<OwnedItem>? house = null) =>
        new(Profile, [], items, house ?? [], DateTimeOffset.UnixEpoch);

    [Fact]
    public void Every_owned_item_becomes_an_inventory_item_with_its_name_and_type()
    {
        var inventory = SnapshotInventory.ToCharacterInventory(Snapshot([Item("Fictional Blade", "Sword", 3), Item("Fictional Cape", "Cape")]));

        Assert.Equal(2, inventory.Count);
        var blade = inventory.Find("Fictional Blade")!;
        Assert.Equal("Fictional Blade", blade.Name);
        Assert.Equal("Sword", blade.Type);
        Assert.Equal(3, blade.Count);
        Assert.True(inventory.Has("Fictional Cape"));
    }

    [Fact]
    public void Bag_bank_and_house_items_are_all_included()
    {
        var inventory = SnapshotInventory.ToCharacterInventory(Snapshot(
            [Item("In Bag"), Item("In Bank", flags: ItemFlags.Bank)],
            [Item("In House", "House Item", flags: ItemFlags.House)]));

        Assert.Equal(3, inventory.Count);
        Assert.True(inventory.Has("In Bag"));
        Assert.True(inventory.Has("In Bank"));
        Assert.True(inventory.Has("In House"));
    }

    [Fact]
    public void A_class_has_a_count_of_zero_because_class_points_are_not_captured()
    {
        var inventory = SnapshotInventory.ToCharacterInventory(Snapshot([Item("Fictional Class", "Class", 1), Item("Other Class", "class", 1)]));

        Assert.Equal(0, inventory.Find("Fictional Class")!.Count);
        Assert.Equal(0, inventory.Find("Other Class")!.Count);
        Assert.True(inventory.Has("Fictional Class"));
    }

    [Fact]
    public void The_coins_flag_is_AC_and_the_upgrade_flag_is_member_only()
    {
        var inventory = SnapshotInventory.ToCharacterInventory(Snapshot(
        [
            Item("Plain"),
            Item("AC Item", flags: ItemFlags.Coins | ItemFlags.Equipped),
            Item("Member Item", flags: ItemFlags.Upgrade),
            Item("Both", flags: ItemFlags.Coins | ItemFlags.Upgrade),
        ]));

        Assert.Equal((false, false), (inventory.Find("Plain")!.IsAc, inventory.Find("Plain")!.IsMemberOnly));
        Assert.Equal((true, false), (inventory.Find("AC Item")!.IsAc, inventory.Find("AC Item")!.IsMemberOnly));
        Assert.Equal((false, true), (inventory.Find("Member Item")!.IsAc, inventory.Find("Member Item")!.IsMemberOnly));
        Assert.Equal((true, true), (inventory.Find("Both")!.IsAc, inventory.Find("Both")!.IsMemberOnly));
    }

    [Fact]
    public void A_snapshot_without_items_gives_an_empty_inventory()
    {
        var inventory = SnapshotInventory.ToCharacterInventory(Snapshot([]));

        Assert.Equal(0, inventory.Count);
    }
}
