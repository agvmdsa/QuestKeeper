using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.Inventory;

public class InventoryCardViewModelTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly SidebarViewModel _sidebar = new();

    private InventoryCardViewModel Build() => new(_sidebar);

    private static readonly string[] Types = ["Sword", "Armor", "Resource", "Pet", "House", "Cape", "Helm"];

    /// <summary>Items 1..count with types cycling through seven names, so seven or more items have seven types.</summary>
    private static CharacterInventory Many(int count) =>
        new(Enumerable.Range(1, count)
            .Select(i => new InventoryItem($"Test Item {i}", Types[(i - 1) % Types.Length], i, false, false))
            .ToList());

    [Fact]
    public void Constants_have_the_agreed_texts()
    {
        Assert.Equal("Loading inventory...", InventoryCardViewModel.LoadingText);
        Assert.Equal("Could not load the inventory. Try Refresh again.", InventoryCardViewModel.UnavailableText);
        Assert.Equal("No inventory found for this character.", InventoryCardViewModel.NotFoundText);
        Assert.Equal("Inventory", InventoryCardViewModel.SidebarTitle);
    }

    [Fact]
    public void Starts_empty()
    {
        var card = Build();

        Assert.False(card.HasData);
        Assert.Empty(card.TypePreview);
        Assert.Empty(card.FilteredItems);
        Assert.Equal("0 items", card.CountText);
        Assert.Equal(string.Empty, card.Status);
        Assert.False(card.HasStatus);
        Assert.Equal(string.Empty, card.FilterText);
    }

    [Fact]
    public void Count_text_is_singular_only_for_one()
    {
        var card = Build();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Data = Many(1);
        Assert.Equal("1 item", card.CountText);
        card.Data = Many(7);
        Assert.Equal("7 items", card.CountText);
        card.Data = Many(0);
        Assert.Equal("0 items", card.CountText);
        Assert.Contains(nameof(InventoryCardViewModel.CountText), changed);
    }

    [Fact]
    public void Type_preview_is_the_five_biggest_types_as_type_dot_count()
    {
        var card = Build();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Data = Many(9);

        Assert.True(card.HasData);
        // Sword and Armor hold two items each (items 1/8 and 2/9), the rest one each, ordered by name.
        Assert.Equal(["Armor · 2", "Sword · 2", "Cape · 1", "Helm · 1", "House · 1"], card.TypePreview);
        Assert.Contains(nameof(InventoryCardViewModel.HasData), changed);
        Assert.Contains(nameof(InventoryCardViewModel.TypePreview), changed);
        Assert.Contains(nameof(InventoryCardViewModel.FilteredItems), changed);
    }

    [Fact]
    public void Data_without_items_has_no_data_and_cannot_open_the_sidebar()
    {
        var card = Build();

        card.Data = Many(0);

        Assert.False(card.HasData);
        Assert.False(card.OpenDetailsCommand.CanExecute(null));
    }

    [Fact]
    public void Status_drives_has_status()
    {
        var card = Build();

        card.Status = InventoryCardViewModel.LoadingText;
        Assert.True(card.HasStatus);
        card.Status = string.Empty;
        Assert.False(card.HasStatus);
    }

    [Fact]
    public void Updated_text_uses_local_time_and_is_empty_without_a_read_time()
    {
        var card = Build();
        Assert.Equal(string.Empty, card.UpdatedText);
        Assert.False(card.HasUpdatedText);

        card.ReadAt = At;

        Assert.Equal("Last updated " + At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), card.UpdatedText);
        Assert.True(card.HasUpdatedText);
    }

    [Fact]
    public void Open_details_can_execute_only_with_items_and_tells_when_it_changes()
    {
        var card = Build();
        var raised = 0;
        card.OpenDetailsCommand.CanExecuteChanged += (_, _) => raised++;
        Assert.False(card.OpenDetailsCommand.CanExecute(null));

        card.Data = Many(3);

        Assert.True(card.OpenDetailsCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public void Open_details_opens_the_sidebar_with_the_card_itself()
    {
        var card = Build();
        card.Data = Many(3);

        card.OpenDetailsCommand.Execute(null);

        Assert.True(_sidebar.IsOpen);
        Assert.Equal("Inventory", _sidebar.Title);
        Assert.Equal("Inventory details", _sidebar.AutomationName);
        Assert.Same(card, _sidebar.Content);
    }

    [Fact]
    public void Clearing_the_data_closes_the_sidebar_when_it_shows_this_card()
    {
        var card = Build();
        card.Data = Many(3);
        card.OpenDetailsCommand.Execute(null);

        card.Data = null;

        Assert.False(_sidebar.IsOpen);
        Assert.False(card.OpenDetailsCommand.CanExecute(null));
    }

    [Fact]
    public void Data_that_becomes_empty_closes_the_sidebar_when_it_shows_this_card()
    {
        var card = Build();
        card.Data = Many(3);
        card.OpenDetailsCommand.Execute(null);

        card.Data = Many(0);

        Assert.False(_sidebar.IsOpen);
    }

    [Fact]
    public void Clearing_the_data_leaves_the_sidebar_open_when_it_shows_something_else()
    {
        var card = Build();
        card.Data = Many(3);
        _sidebar.Open("Equipped", new object());

        card.Data = null;

        Assert.True(_sidebar.IsOpen);
        Assert.Equal("Equipped", _sidebar.Title);
    }

    [Fact]
    public void Filtered_items_are_sorted_by_type_then_name_ignoring_case()
    {
        var card = Build();

        card.Data = new CharacterInventory(
        [
            new InventoryItem("zeta", "Sword", 1, false, false),
            new InventoryItem("Beta", "sword", 1, false, false),
            new InventoryItem("Alpha", "Armor", 1, false, false),
            new InventoryItem("alpha", "Sword", 1, false, false),
        ]);

        Assert.Equal(["Alpha", "alpha", "Beta", "zeta"], card.FilteredItems.Select(i => i.Name));
    }

    [Fact]
    public void The_filter_matches_names_containing_the_text_ignoring_case_and_surrounding_spaces()
    {
        var card = Build();
        card.Data = Many(30);
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.FilterText = "  ITEM 1";

        Assert.Equal(11, card.FilteredItems.Count);
        Assert.All(card.FilteredItems, item => Assert.Contains("item 1", item.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nameof(InventoryCardViewModel.FilteredItems), changed);
        Assert.Contains(nameof(InventoryCardViewModel.TotalText), changed);
    }

    [Fact]
    public void An_empty_filter_shows_everything_and_a_filter_without_matches_shows_nothing()
    {
        var card = Build();
        card.Data = Many(30);

        card.FilterText = "nothing like this";
        Assert.Empty(card.FilteredItems);
        Assert.True(card.HasNoMatches);
        Assert.Equal("0 of 30 items", card.TotalText);

        card.FilterText = "   ";
        Assert.Equal(30, card.FilteredItems.Count);
        Assert.False(card.HasNoMatches);
        Assert.Equal("30 items", card.TotalText);
    }

    [Fact]
    public void The_total_text_says_how_many_match_while_filtering()
    {
        var card = Build();
        card.Data = Many(30);

        card.FilterText = "item 2";

        Assert.Equal("11 of 30 items", card.TotalText);
    }

    [Fact]
    public void New_data_is_filtered_by_the_text_that_is_already_typed()
    {
        var card = Build();
        card.Data = Many(30);
        card.FilterText = "item 3";

        card.Data = Many(40);

        Assert.Equal(11, card.FilteredItems.Count);
    }

    [Fact]
    public void Thousands_of_items_are_filtered_without_trouble()
    {
        var card = Build();
        card.Data = Many(20000);

        card.FilterText = "item 1999";

        Assert.Equal(11, card.FilteredItems.Count);
    }

    [Fact]
    public void Clear_resets_data_read_time_status_and_filter_and_closes_its_sidebar()
    {
        var card = Build();
        card.Data = Many(3);
        card.ReadAt = At;
        card.Status = "y";
        card.FilterText = "item";
        card.OpenDetailsCommand.Execute(null);

        card.Clear();

        Assert.Null(card.Data);
        Assert.Null(card.ReadAt);
        Assert.Equal(string.Empty, card.Status);
        Assert.Equal(string.Empty, card.FilterText);
        Assert.Empty(card.FilteredItems);
        Assert.False(_sidebar.IsOpen);
    }

    [Fact]
    public void Loading_another_character_resets_the_filter_so_the_new_list_is_not_hidden()
    {
        var card = Build();
        card.Data = Many(7);
        card.FilterText = "no such item";
        Assert.True(card.HasNoMatches);

        card.Data = null;
        card.Data = new CharacterInventory([new InventoryItem("Other Hero Sword", "Sword", 1, false, false)]);

        Assert.Equal(string.Empty, card.FilterText);
        Assert.False(card.HasNoMatches);
        Assert.Single(card.FilteredItems);
    }
}
