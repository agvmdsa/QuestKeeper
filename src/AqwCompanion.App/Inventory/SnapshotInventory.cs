using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.Inventory;

/// <summary>Turns the items a Capture read into the inventory shape the rest of the app uses.</summary>
public static class SnapshotInventory
{
    /// <summary>
    /// One <see cref="InventoryItem"/> per owned item (bag, bank and house together). The count is the quantity, except for
    /// classes: their class points are not captured, so their count is 0. AC is the coins flag, member-only the upgrade flag.
    /// </summary>
    public static CharacterInventory ToCharacterInventory(CharacterSnapshot snapshot) =>
        new([.. snapshot.Items.Concat(snapshot.HouseItems).Select(ToInventoryItem)]);

    private static InventoryItem ToInventoryItem(OwnedItem item) => new(
        item.Name,
        item.Type,
        string.Equals(item.Type, CharacterInventory.ClassTypeName, StringComparison.OrdinalIgnoreCase) ? 0 : item.Quantity,
        item.Flags.HasFlag(ItemFlags.Coins),
        item.Flags.HasFlag(ItemFlags.Upgrade));
}
