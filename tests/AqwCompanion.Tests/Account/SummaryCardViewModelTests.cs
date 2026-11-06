using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.Core.Account;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Account;

public class SummaryCardViewModelTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Without_data_the_card_texts_are_dashes()
    {
        var card = new SummaryCardViewModel();

        Assert.False(card.HasData);
        Assert.Equal(SummaryCardViewModel.MissingValueText, card.AdventureCoinsText);
        Assert.Equal(SummaryCardViewModel.MissingValueText, card.AccountTypeText);
        Assert.Equal(SummaryCardViewModel.MissingValueText, card.MembershipText);
        Assert.Equal(SummaryCardViewModel.MissingValueText, card.GearsOfDoomText);
    }

    [Fact]
    public void Constants_have_the_agreed_texts()
    {
        Assert.Equal("Not available. Try Refresh.", SummaryCardViewModel.UnavailableText);
        Assert.Equal("—", SummaryCardViewModel.MissingValueText);
    }

    [Fact]
    public void All_fields_show_with_thousands_separators()
    {
        var card = new SummaryCardViewModel { Data = SampleAccount.Summary };

        Assert.True(card.HasData);
        Assert.Equal("1,234,567", card.AdventureCoinsText);
        Assert.Equal("LEGEND", card.AccountTypeText);
        Assert.Equal("Membership Expired: Tuesday, January 2, 2030", card.MembershipText);
        Assert.Equal("7", card.GearsOfDoomText);
    }

    [Fact]
    public void Thousands_separator_uses_the_invariant_culture()
    {
        var card = new SummaryCardViewModel { Data = new AccountSummary(9585, null, null, null) };

        Assert.Equal("9,585", card.AdventureCoinsText);
    }

    [Fact]
    public void Missing_fields_show_a_dash_while_the_others_show()
    {
        var card = new SummaryCardViewModel { Data = new AccountSummary(5, null, null, null) };

        Assert.True(card.HasData);
        Assert.Equal("5", card.AdventureCoinsText);
        Assert.Equal(SummaryCardViewModel.MissingValueText, card.AccountTypeText);
        Assert.Equal(SummaryCardViewModel.MissingValueText, card.MembershipText);
        Assert.Equal(SummaryCardViewModel.MissingValueText, card.GearsOfDoomText);
    }

    [Fact]
    public void Setting_data_raises_change_notifications_for_the_texts()
    {
        var card = new SummaryCardViewModel();
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Data = SampleAccount.Summary;

        Assert.Contains(nameof(SummaryCardViewModel.HasData), changed);
        Assert.Contains(nameof(SummaryCardViewModel.AdventureCoinsText), changed);
        Assert.Contains(nameof(SummaryCardViewModel.AccountTypeText), changed);
        Assert.Contains(nameof(SummaryCardViewModel.MembershipText), changed);
        Assert.Contains(nameof(SummaryCardViewModel.GearsOfDoomText), changed);
    }

    [Fact]
    public void Updated_text_is_empty_without_a_read_time_and_local_time_with_one()
    {
        var card = new SummaryCardViewModel();
        Assert.Equal(string.Empty, card.UpdatedText);
        Assert.False(card.HasUpdatedText);
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.ReadAt = At;

        Assert.Equal("Last updated " + At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), card.UpdatedText);
        Assert.True(card.HasUpdatedText);
        Assert.Contains(nameof(SummaryCardViewModel.UpdatedText), changed);
        Assert.Contains(nameof(SummaryCardViewModel.HasUpdatedText), changed);
    }

    [Fact]
    public void Clear_resets_data_and_read_time()
    {
        var card = new SummaryCardViewModel { Data = SampleAccount.Summary, ReadAt = At };

        card.Clear();

        Assert.Null(card.Data);
        Assert.Null(card.ReadAt);
        Assert.False(card.HasData);
        Assert.Equal(string.Empty, card.UpdatedText);
    }
}
