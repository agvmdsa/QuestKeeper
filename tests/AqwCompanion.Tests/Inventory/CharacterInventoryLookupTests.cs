using System.Text.Json;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.Inventory;

public class CharacterInventoryLookupTests
{
    private static CharacterInventory Thousands(int count) =>
        new([.. Enumerable.Range(0, count).Select(i =>
            new InventoryItem($"  Test   Item {i} ", i % 2 == 0 ? "Sword" : "Armor", i % 7 + 1, false, false))]);

    [Fact]
    public void Repeated_lookups_in_a_large_inventory_give_the_right_answers()
    {
        var inventory = Thousands(5000);

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 5000; i += 37)
            {
                Assert.Equal(i % 7 + 1, inventory.CountOf($"test item {i}"));
                Assert.Equal(i % 7 + 1, inventory.CountOf($"TEST ITEM   {i}  "));
                Assert.True(inventory.Has($"Test Item {i}"));
                Assert.Same(inventory.Items[i], inventory.Find($" test item {i}"));
            }

            Assert.Equal(0, inventory.CountOf("Test Item 5000"));
            Assert.False(inventory.Has("Test Item 5000"));
            Assert.Null(inventory.Find("nothing"));
            Assert.Equal(0, inventory.CountOf("   "));
            Assert.Null(inventory.Find(""));
        }
    }

    [Fact]
    public void Duplicates_are_summed_and_find_returns_the_first()
    {
        var first = new InventoryItem("Guardian Blade", "Sword", 2, false, false);
        var inventory = new CharacterInventory([first, new InventoryItem("guardian  blade", "Wall Item", 3, false, false)]);

        Assert.Equal(5, inventory.CountOf("GUARDIAN BLADE"));
        Assert.Same(first, inventory.Find("Guardian Blade"));
    }

    [Fact]
    public void Summed_counts_saturate_at_int_max()
    {
        var inventory = new CharacterInventory(
            [new InventoryItem("Big", "Item", int.MaxValue, false, false), new InventoryItem("big", "Item", 5, false, false)]);

        Assert.Equal(int.MaxValue, inventory.CountOf("Big"));
    }

    [Fact]
    public void The_lookup_index_is_not_serialized_and_a_round_trip_still_looks_up()
    {
        var inventory = Thousands(50);
        _ = inventory.CountOf("test item 3");

        var json = JsonSerializer.Serialize(inventory, StoredJson.Options);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(["items"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        var loaded = JsonSerializer.Deserialize<CharacterInventory>(json, StoredJson.Options)!;
        Assert.Equal(inventory.Items, loaded.Items);
        Assert.Equal(3 % 7 + 1, loaded.CountOf("test item 3"));
        Assert.Equal(inventory.Items.Count, loaded.Count);
    }

    [Fact]
    public void Equality_ignores_whether_the_index_was_built()
    {
        var items = new[] { new InventoryItem("A", "Sword", 1, false, false) };
        var a = new CharacterInventory(items);
        var b = new CharacterInventory(items);
        _ = a.CountOf("a");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
