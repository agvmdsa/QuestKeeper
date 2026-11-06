using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.Inventory;

public class NameKeyCompactTests
{
    [Theory]
    [InlineData("Evolved Shadow of Nulgath", "evolvedshadowofnulgath")]
    [InlineData("evolved-shadow-of-nulgath", "evolvedshadowofnulgath")]
    [InlineData("Void Paladin (Class)", "voidpaladinclass")]
    [InlineData("Blade of Awe 2", "bladeofawe2")]
    [InlineData("Test Blade (0 AC)", "testblade0ac")]
    [InlineData("Ça  Va", "çava")]
    [InlineData("  ---  ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Only_lower_case_letters_and_digits_are_kept(string? name, string expected)
    {
        Assert.Equal(expected, NameKey.Compact(name));
    }

    [Fact]
    public void Compacting_twice_changes_nothing()
    {
        var once = NameKey.Compact("Void Crystal A (x2)");

        Assert.Equal(once, NameKey.Compact(once));
    }
}

public class CharacterInventoryCompactKeyTests
{
    private static CharacterInventory Inventory(params string[] names) =>
        new(names.Select(n => new InventoryItem(n, "Armor", 1, false, false)).ToList());

    [Fact]
    public void A_compact_key_is_found_whatever_the_spacing_case_or_punctuation()
    {
        var inventory = Inventory("Evolved Shadow of Nulgath", "Void-Paladin");

        Assert.True(inventory.HasCompactKey("evolvedshadowofnulgath"));
        Assert.True(inventory.HasCompactKey("voidpaladin"));
        Assert.True(inventory.HasCompactKey("Void Paladin"));
    }

    [Fact]
    public void Whole_keys_only_never_a_part_of_one()
    {
        var inventory = Inventory("Cyber Void Paladin");

        Assert.False(inventory.HasCompactKey("voidpaladin"));
        Assert.False(inventory.HasCompactKey("cyber"));
    }

    [Fact]
    public void An_empty_key_is_never_found_even_when_an_item_name_has_no_letters()
    {
        var inventory = Inventory("---", "");

        Assert.False(inventory.HasCompactKey(""));
        Assert.False(inventory.HasCompactKey("  -- "));
    }

    [Fact]
    public async Task Concurrent_lookups_on_one_inventory_all_agree()
    {
        var inventory = Inventory(Enumerable.Range(0, 500).Select(i => $"Item {i}").ToArray());

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() => Enumerable.Range(0, 500).All(i => inventory.HasCompactKey($"item{i}")))));

        Assert.All(results, Assert.True);
    }

    [Fact]
    public void Equal_inventories_stay_equal_after_a_lookup_on_one_of_them()
    {
        var a = Inventory("A");
        var b = a with { };
        _ = a.HasCompactKey("a");

        Assert.Equal(a, b);
    }
}
