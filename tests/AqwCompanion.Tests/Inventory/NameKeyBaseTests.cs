using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.Inventory;

public class NameKeyBaseTests
{
    [Theory]
    [InlineData("The Collector (Class) (Merge)", "thecollector")]
    [InlineData("Pyromancer (Class) (Merge)", "pyromancer")]
    [InlineData("Cryomancer (Merge)", "cryomancer")]
    [InlineData("Lord Of Order (Quest)", "lordoforder")]
    [InlineData("Some Armor (Daily Quest)", "somearmor")]
    [InlineData("Test Blade (AC)", "testblade")]
    [InlineData("Test Blade (0 AC)", "testblade")]
    [InlineData("Test Blade (Non-AC)", "testblade")]
    [InlineData("Test Blade (non ac)", "testblade")]
    [InlineData("Test  Blade   (  class  )", "testblade")]
    [InlineData("Plain Name", "plainname")]
    public void Trailing_acquisition_and_ac_qualifiers_are_dropped_however_many_there_are(string name, string expected)
    {
        Assert.Equal(expected, NameKey.Base(name));
    }

    [Theory]
    [InlineData("Test Blade (Rare)", "testbladerare")]
    [InlineData("Test (Class) Blade", "testclassblade")]
    [InlineData("Test Blade (Rare) (AC)", "testbladerare")]
    public void Other_qualifiers_and_qualifiers_in_the_middle_are_kept(string name, string expected)
    {
        Assert.Equal(expected, NameKey.Base(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(Class)")]
    [InlineData("(Class) (Merge)")]
    public void A_name_with_nothing_but_qualifiers_has_no_base_key(string? name)
    {
        Assert.Equal(string.Empty, NameKey.Base(name));
    }
}

public class CharacterInventoryBaseKeyTests
{
    private static CharacterInventory Inventory(params string[] names) =>
        new(names.Select(n => new InventoryItem(n, "Armor", 1, false, false)).ToList());

    [Fact]
    public void A_base_key_is_found_whatever_qualifiers_either_side_has()
    {
        var inventory = Inventory("The Collector", "Test Blade (AC)");

        Assert.True(inventory.HasBaseKey("The Collector (Class) (Merge)"));
        Assert.True(inventory.HasBaseKey("Test Blade"));
        Assert.True(inventory.HasBaseKey("Test Blade (0 AC)"));
    }

    [Fact]
    public void Whole_base_keys_only_and_never_an_empty_one()
    {
        var inventory = Inventory("Cyber Void Paladin", "(Class)");

        Assert.False(inventory.HasBaseKey("Void Paladin"));
        Assert.False(inventory.HasBaseKey("(Merge)"));
        Assert.False(inventory.HasBaseKey(""));
    }
}
