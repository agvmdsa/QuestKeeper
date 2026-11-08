using AqwCompanion.App.Account;
using AqwCompanion.App.Inventory;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Builds an <see cref="ActiveInventory"/> over an idle capture, so the site's inventory is what it shows.</summary>
public static class ActiveInventoryBuilder
{
    public static ActiveInventory For(InventoryCardViewModel site) => new(site, new CaptureRig().CreateViewModel());

    public static ActiveInventory Empty() => For(new InventoryCardViewModel(new SidebarViewModel()));
}
