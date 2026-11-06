using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.HardFarms;

public class OwnershipMatcherTests
{
    // A page name that matches none of the names used below, so only the display name decides in the older cases.
    private const string Slug = "zz-unrelated-page";

    private static CharacterInventory Inventory(params string[] names) =>
        new(names.Select(n => new InventoryItem(n, "Armor", 1, false, false)).ToList());

    [Fact]
    public void An_exact_name_is_owned_ignoring_case_and_spacing()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match(Slug, "Test  Blade", Inventory("test blade")));
    }

    [Fact]
    public void One_trailing_class_qualifier_is_stripped_on_the_retry()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match(Slug, "Void Warrior (Class)", Inventory("Void Warrior")));
    }

    [Fact]
    public void The_exact_name_with_the_qualifier_wins_before_any_stripping()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match(Slug, "Void Warrior (Class)", Inventory("Void Warrior (Class)")));
    }

    [Fact]
    public void Only_one_qualifier_is_stripped()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(Slug, "Test (Class) (Class)", Inventory("Test")));
    }

    [Theory]
    [InlineData("Test Blade (AC)", "Test Blade")]
    [InlineData("Test Blade (0 AC)", "Test Blade")]
    [InlineData("Test Blade (Rare)", "Test Blade")]
    [InlineData("Test Blade", "Test Blade (AC)")]
    public void Other_qualifiers_are_never_stripped(string wikiName, string owned)
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(Slug, wikiName, Inventory(owned)));
    }

    [Fact]
    public void A_name_that_is_only_the_qualifier_does_not_match_anything()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(Slug, "(Class)", Inventory("")));
    }

    [Fact]
    public void Missing_needs_a_known_name_and_a_loaded_inventory()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(Slug, "Rare Thing", Inventory("Other Thing")));
        Assert.Equal(Ownership.Unknown, OwnershipMatcher.Match(Slug, "Rare Thing", null));
        Assert.Equal(Ownership.Unknown, OwnershipMatcher.Match(Slug, null, null));
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(Slug, null, Inventory("Other Thing")));
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(Slug, "  ", Inventory("Other Thing")));
    }

    [Fact]
    public void The_slug_matches_when_the_wiki_name_is_unknown()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match("evolved-shadow-of-nulgath", null, Inventory("Evolved Shadow of Nulgath")));
    }

    [Fact]
    public void The_slug_matches_when_the_wiki_name_differs()
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match("evolved-shadow-of-nulgath", "Evolved Shadow", Inventory("Evolved Shadow of Nulgath")));
    }

    [Theory]
    [InlineData("evolved-legacy-of-nulgath-quest", "Evolved Legacy of Nulgath")]
    [InlineData("void-highlord-class", "Void HighLord")]
    [InlineData("some-armor-merge", "Some Armor")]
    [InlineData("some-armor-classmerge", "Some Armor")]
    [InlineData("evolved-legacy-of-nulgath-quest", "Evolved Legacy of Nulgath Quest")]
    public void One_trailing_page_type_word_of_the_slug_is_also_tried(string slug, string owned)
    {
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match(slug, null, Inventory(owned)));
    }

    [Fact]
    public void Only_one_page_type_word_is_removed_from_the_slug()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match("a-class-quest", null, Inventory("A")));
        Assert.Equal(Ownership.Owned, OwnershipMatcher.Match("a-class-quest", null, Inventory("A Class")));
    }

    [Fact]
    public void A_slug_that_is_only_a_page_type_word_matches_nothing_empty()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match("quest", null, Inventory("---", "")));
    }

    [Theory]
    [InlineData("void-paladin", "Void Paladin", "Cyber Void Paladin")]
    [InlineData("evolved-shadow-of-nulgath", "Evolved Shadow of Nulgath", "Empowered Evolved Shadow")]
    [InlineData("evolved-shadow-of-nulgath", "Evolved Shadow of Nulgath", "Evolved Shadow of Nulgath Pet")]
    [InlineData("void-paladin", null, "Void Paladins")]
    public void Matching_is_by_whole_key_never_by_a_part(string slug, string? wikiName, string owned)
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(slug, wikiName, Inventory(owned)));
    }

    [Fact]
    public void The_slug_never_strips_the_ac_qualifiers_of_the_name_either()
    {
        Assert.Equal(Ownership.Missing, OwnershipMatcher.Match(Slug, "Test Blade (AC)", Inventory("Test Blade")));
    }

    [Fact]
    public void An_unloaded_inventory_is_unknown_whatever_the_names()
    {
        Assert.Equal(Ownership.Unknown, OwnershipMatcher.Match("void-paladin", "Void Paladin", null));
    }
}
