using AqwCompanion.App.Inventory;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Builds an <see cref="ActiveInventory"/> over an idle capture, so it has no inventory until something is captured.</summary>
public static class ActiveInventoryBuilder
{
    public static IActiveInventory Empty() => new FakeActiveInventory();
}
