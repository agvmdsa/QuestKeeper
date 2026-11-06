using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.HardFarms;

public class OwnershipMatcherTests
{
    private static CharacterInventory Inventory(params string[] names) =>
        new(names.Select(n => new InventoryItem(n, "Armor", 1, false, false)).ToList());

    [Fact]
    public void An_exact_name_is_owned_ignoring_case_and_spacing()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match("Test  Blade", Inventory("test blade")));
    }

    [Fact]
    public void One_trailing_class_qualifier_is_stripped_on_the_retry()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match("Void Warrior (Class)", Inventory("Void Warrior")));
    }

    [Fact]
    public void The_exact_name_with_the_qualifier_wins_before_any_stripping()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match("Void Warrior (Class)", Inventory("Void Warrior (Class)")));
    }

    [Fact]
    public void Only_one_qualifier_is_stripped()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match("Test (Class) (Class)", Inventory("Test")));
    }

    [Theory]
    [InlineData("Test Blade (AC)", "Test Blade")]
    [InlineData("Test Blade (0 AC)", "Test Blade")]
    [InlineData("Test Blade (Rare)", "Test Blade")]
    [InlineData("Test Blade", "Test Blade (AC)")]
    public void Other_qualifiers_are_never_stripped(string wikiName, string owned)
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(wikiName, Inventory(owned)));
    }

    [Fact]
    public void A_name_that_is_only_the_qualifier_does_not_match_anything()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match("(Class)", Inventory("")));
    }

    [Fact]
    public void Missing_needs_a_known_name_and_a_loaded_inventory()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match("Rare Thing", Inventory("Other Thing")));
        Assert.Equal(Ownership.Unknown, OwnershipMatcher.Match("Rare Thing", null));
        Assert.Equal(Ownership.Unknown, OwnershipMatcher.Match(null, Inventory("Other Thing")));
        Assert.Equal(Ownership.Unknown, OwnershipMatcher.Match("  ", Inventory("Other Thing")));
    }
}
