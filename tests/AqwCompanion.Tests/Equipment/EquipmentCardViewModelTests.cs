using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.App.Equipment;
using AqwCompanion.Core.Equipment;

namespace AqwCompanion.Tests.Equipment;

public class EquipmentCardViewModelTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly SidebarViewModel _sidebar = new();

    private EquipmentCardViewModel Build() => new(_sidebar);

    private static CharacterEquipment Gear(int standardSlots = 7) =>
        new("Test Hero", 10, null, null, null, null, null,
            [.. Enumerable.Range(1, standardSlots).Select(i => new EquipmentSlot($"Slot {i}", $"Item {i}", null, false)),
                new EquipmentSlot("Cape", "Custom Cape", null, true)],
            [], 3);

    [Fact]
    public void Constants_have_the_agreed_texts()
    {
        Assert.Equal("Loading equipment...", EquipmentCardViewModel.LoadingText);
        Assert.Equal("Could not load the character page. Try Refresh again.", EquipmentCardViewModel.UnavailableText);
        Assert.Equal("No equipment found on the character page.", EquipmentCardViewModel.NotFoundText);
        Assert.Equal("Equipped", EquipmentCardViewModel.SidebarTitle);
    }

    [Fact]
    public void Starts_empty()
    {
        var card = Build();

        Assert.False(card.HasData);
        Assert.Empty(card.PreviewSlots);
        Assert.Equal(string.Empty, card.Status);
        Assert.False(card.HasStatus);
        Assert.False(card.HasUpdatedText);
        Assert.Equal(string.Empty, card.UpdatedText);
    }

    [Fact]
    public void Preview_slots_are_the_first_five_standard_slots()
    {
        var card = Build();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Data = Gear();

        Assert.True(card.HasData);
        Assert.Equal(["Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5"], card.PreviewSlots.Select(s => s.Label));
        Assert.Contains(nameof(EquipmentCardViewModel.HasData), changed);
        Assert.Contains(nameof(EquipmentCardViewModel.PreviewSlots), changed);
    }

    [Fact]
    public void Status_drives_has_status()
    {
        var card = Build();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Status = EquipmentCardViewModel.LoadingText;

        Assert.True(card.HasStatus);
        Assert.Contains(nameof(EquipmentCardViewModel.HasStatus), changed);
        card.Status = string.Empty;
        Assert.False(card.HasStatus);
    }

    [Fact]
    public void Updated_text_uses_local_time_and_is_empty_without_a_read_time()
    {
        var card = Build();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.ReadAt = At;

        Assert.Equal("Last updated " + At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), card.UpdatedText);
        Assert.True(card.HasUpdatedText);
        Assert.Contains(nameof(EquipmentCardViewModel.UpdatedText), changed);
        Assert.Contains(nameof(EquipmentCardViewModel.HasUpdatedText), changed);
        card.ReadAt = null;
        Assert.Equal(string.Empty, card.UpdatedText);
        Assert.False(card.HasUpdatedText);
    }

    [Fact]
    public void Open_details_can_execute_only_with_data_and_tells_when_it_changes()
    {
        var card = Build();
        var raised = 0;
        card.OpenDetailsCommand.CanExecuteChanged += (_, _) => raised++;
        Assert.False(card.OpenDetailsCommand.CanExecute(null));

        card.Data = Gear();

        Assert.True(card.OpenDetailsCommand.CanExecute(null));
        Assert.True(raised > 0);
        raised = 0;
        card.Data = null;
        Assert.False(card.OpenDetailsCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public void Open_details_opens_the_sidebar_with_the_card_itself()
    {
        var card = Build();
        card.Data = Gear();

        card.OpenDetailsCommand.Execute(null);

        Assert.True(_sidebar.IsOpen);
        Assert.Equal("Equipped", _sidebar.Title);
        Assert.Equal("Equipped details", _sidebar.AutomationName);
        Assert.Same(card, _sidebar.Content);
    }

    [Fact]
    public void Clearing_the_data_closes_the_sidebar_when_it_shows_this_card()
    {
        var card = Build();
        card.Data = Gear();
        card.OpenDetailsCommand.Execute(null);

        card.Data = null;

        Assert.False(_sidebar.IsOpen);
    }

    [Fact]
    public void Clearing_the_data_leaves_the_sidebar_open_when_it_shows_something_else()
    {
        var card = Build();
        card.Data = Gear();
        _sidebar.Open("Badges", new object());

        card.Data = null;

        Assert.True(_sidebar.IsOpen);
        Assert.Equal("Badges", _sidebar.Title);
    }

    [Fact]
    public void Clear_resets_data_read_time_and_status_and_closes_its_sidebar()
    {
        var card = Build();
        card.Data = Gear();
        card.ReadAt = At;
        card.Status = "x";
        card.OpenDetailsCommand.Execute(null);

        card.Clear();

        Assert.Null(card.Data);
        Assert.Null(card.ReadAt);
        Assert.Equal(string.Empty, card.Status);
        Assert.False(card.HasStatus);
        Assert.False(_sidebar.IsOpen);
    }
}
