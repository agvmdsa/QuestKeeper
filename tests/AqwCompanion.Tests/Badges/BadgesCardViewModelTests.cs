using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.App.Badges;
using AqwCompanion.Core.Badges;

namespace AqwCompanion.Tests.Badges;

public class BadgesCardViewModelTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly SidebarViewModel _sidebar = new();

    private BadgesCardViewModel Build() => new(_sidebar);

    private static CharacterBadges Many(int count) =>
        new(Enumerable.Range(1, count)
            .Select(i => new Badge($"Test Badge {i}", $"Description {i}", i <= 2 ? "Quests" : "Events"))
            .ToList());

    [Fact]
    public void Constants_have_the_agreed_texts()
    {
        Assert.Equal("Loading badges...", BadgesCardViewModel.LoadingText);
        Assert.Equal("Could not load the badges. Try Refresh again.", BadgesCardViewModel.UnavailableText);
        Assert.Equal("No badges found for this character.", BadgesCardViewModel.NotFoundText);
        Assert.Equal("Badges", BadgesCardViewModel.SidebarTitle);
    }

    [Fact]
    public void Starts_empty()
    {
        var card = Build();

        Assert.False(card.HasData);
        Assert.Empty(card.Preview);
        Assert.Empty(card.Groups);
        Assert.Equal("0 badges", card.CountText);
        Assert.Equal(string.Empty, card.Status);
        Assert.False(card.HasStatus);
    }

    [Fact]
    public void Count_text_is_singular_only_for_one()
    {
        var card = Build();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Data = Many(1);
        Assert.Equal("1 badge", card.CountText);
        card.Data = Many(7);
        Assert.Equal("7 badges", card.CountText);
        card.Data = Many(0);
        Assert.Equal("0 badges", card.CountText);
        Assert.Contains(nameof(BadgesCardViewModel.CountText), changed);
    }

    [Fact]
    public void Preview_is_five_and_groups_are_by_category()
    {
        var card = Build();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Data = Many(7);

        Assert.True(card.HasData);
        Assert.Equal(5, card.Preview.Count());
        Assert.Equal("Test Badge 1", card.Preview.First().Title);
        Assert.Equal(["Quests", "Events"], card.Groups.Select(g => g.Category));
        Assert.Contains(nameof(BadgesCardViewModel.HasData), changed);
        Assert.Contains(nameof(BadgesCardViewModel.Preview), changed);
        Assert.Contains(nameof(BadgesCardViewModel.Groups), changed);
    }

    [Fact]
    public void Data_without_badges_has_no_data_and_cannot_open_the_sidebar()
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

        card.Status = BadgesCardViewModel.LoadingText;

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
    public void Open_details_can_execute_only_with_badges_and_tells_when_it_changes()
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
        Assert.Equal("Badges", _sidebar.Title);
        Assert.Equal("Badges details", _sidebar.AutomationName);
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
    public void Clear_resets_data_read_time_and_status_and_closes_its_sidebar()
    {
        var card = Build();
        card.Data = Many(3);
        card.ReadAt = At;
        card.Status = "y";
        card.OpenDetailsCommand.Execute(null);

        card.Clear();

        Assert.Null(card.Data);
        Assert.Null(card.ReadAt);
        Assert.Equal(string.Empty, card.Status);
        Assert.False(_sidebar.IsOpen);
    }
}
