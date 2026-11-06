using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Aqw;

public class AccountHomeParserTests
{
    private const string FakeEmail = "someone@example.com";

    private readonly AccountHomeParser _parser = new();

    private static PageSnapshot Snapshot(string html, string url = AqwUrls.Home) =>
        PageSnapshot.Create(url, html, "1.0.0", DateTimeOffset.UnixEpoch);

    private static PageSnapshot Fixture(string name) =>
        Snapshot(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void Reads_the_name_from_the_character_page_link()
    {
        var result = _parser.Parse(Fixture("account-home-sample.html"));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(new AccountIdentity("Test Hero", SampleAccount.Summary), result.Data);
        Assert.Equal("character-page-link", result.Diagnostics.MatchedStrategy);
        Assert.Equal("account-home", result.Diagnostics.ParserName);
        Assert.Equal("2", result.Diagnostics.ParserVersion);
        Assert.Equal(1, result.Diagnostics.ItemCount);
        Assert.Empty(result.Diagnostics.MissingMarkers);
    }

    [Fact]
    public void Falls_back_to_the_username_row_and_decodes_entities()
    {
        var result = _parser.Parse(Fixture("account-home-username-only.html"));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal("Name With 'Quote' & Spaces", result.Data!.CharacterName);
        Assert.Equal("username-row", result.Diagnostics.MatchedStrategy);
        Assert.Equal(1, result.Diagnostics.ItemCount);
        Assert.Null(result.Data.Summary);
    }

    [Fact]
    public void Link_and_row_strategies_agree_on_the_sample_page()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "account-home-sample.html"));
        var withoutLink = html.Replace("/CharPage?id=", "/Elsewhere?id=", StringComparison.Ordinal);

        var fromLink = _parser.Parse(Snapshot(html));
        var fromRow = _parser.Parse(Snapshot(withoutLink));

        Assert.Equal("username-row", fromRow.Diagnostics.MatchedStrategy);
        Assert.Equal(fromLink.Data, fromRow.Data);
    }

    private static string Card(params string[] rows) =>
        "<div><div class=\"tblHeader\"><h4>Account Info</h4></div>"
        + "<div class=\"row\"><div class=\"fw-bold\">Username:</div><div class=\"col\">Test Hero</div></div>"
        + string.Concat(rows) + "</div>";

    private static string Row(string label, string value) =>
        $"<div class=\"row\"><div class=\"col-12 fw-bold\">{label}</div><div class=\"col\">{value}</div></div>";

    private AccountSummary? SummaryOf(params string[] rows)
    {
        var result = _parser.Parse(Snapshot(Card(rows)));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal("Test Hero", result.Data!.CharacterName);
        return result.Data.Summary;
    }

    private const string CoinsRow = "<div class=\"row\"><div class=\"fw-bold\">AdventureCoins:</div><div class=\"col\">1,234</div></div>";

    [Fact]
    public void Reads_the_three_summary_fields_from_the_sample_page()
    {
        var result = _parser.Parse(Fixture("account-home-sample.html"));

        Assert.Equal(SampleAccount.Summary, result.Data!.Summary);
    }

    [Theory]
    [InlineData("AdventureCoins:")]
    [InlineData("Account Type:")]
    [InlineData("Doom Wheel Gears:")]
    public void A_missing_summary_row_only_leaves_its_fields_null(string omitted)
    {
        var rows = new[]
        {
            Row("AdventureCoins:", "50"),
            Row("Account Type:", "<span>LEGEND</span><br><span>Active</span>"),
            Row("Doom Wheel Gears:", "<div>You have 3 Gears of Doom</div>"),
        };

        var summary = SummaryOf(rows.Where(row => !row.Contains(omitted, StringComparison.Ordinal)).ToArray())!;

        Assert.Equal(omitted == "AdventureCoins:" ? null : 50, summary.AdventureCoins);
        Assert.Equal(omitted == "Account Type:" ? null : "LEGEND", summary.AccountType);
        Assert.Equal(omitted == "Account Type:" ? null : "Active", summary.MembershipStatus);
        Assert.Equal(omitted == "Doom Wheel Gears:" ? null : 3, summary.GearsOfDoom);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12,345", 12345)]
    [InlineData("  1,234,567 \n", 1234567)]
    public void Reads_adventure_coins_with_or_without_thousands_separators(string text, int expected)
    {
        Assert.Equal(expected, SummaryOf(Row("AdventureCoins:", text))!.AdventureCoins);
    }

    [Theory]
    [InlineData("lots")]
    [InlineData("-5")]
    [InlineData("99999999999")]
    [InlineData("")]
    public void Unreadable_adventure_coins_are_null_and_do_not_fail_the_parse(string text)
    {
        Assert.Null(SummaryOf(Row("AdventureCoins:", text)));
    }

    [Theory]
    [InlineData("You have 0 Gears of Doom", 0)]
    [InlineData("You have 1 Gear of Doom", 1)]
    [InlineData("You have 1,200 Gears of Doom", 1200)]
    public void Reads_the_gears_count_from_the_sentence(string text, int expected)
    {
        var value = $"<div>{text} <a href=\"/x\">Learn More</a></div>";

        Assert.Equal(expected, SummaryOf(Row("Doom Wheel Gears:", value))!.GearsOfDoom);
    }

    [Fact]
    public void Gears_text_that_does_not_match_is_null()
    {
        Assert.Null(SummaryOf(Row("Doom Wheel Gears:", "<div>Spin the wheel to find out</div>")));
    }

    [Fact]
    public void An_active_membership_keeps_its_own_line_whatever_its_class()
    {
        var value = "<span class=\"fw-bold text-dark\">LEGEND</span><br><span class=\"text-success\">Active until Friday, March 3, 2031</span>";

        var summary = SummaryOf(Row("Account Type:", value))!;

        Assert.Equal("LEGEND", summary.AccountType);
        Assert.Equal("Active until Friday, March 3, 2031", summary.MembershipStatus);
    }

    [Fact]
    public void An_account_type_without_a_second_span_has_no_membership_line()
    {
        var value = "<span class=\"fw-bold text-dark\">FREE</span><br><a href=\"/AQW/BuyMembership\">Become a Member</a>";

        var summary = SummaryOf(Row("Account Type:", value))!;

        Assert.Equal("FREE", summary.AccountType);
        Assert.Null(summary.MembershipStatus);
    }

    [Fact]
    public void Summary_labels_are_matched_trimmed_and_case_insensitive()
    {
        var summary = SummaryOf(Row("  adventurecoins:  ", "9"), Row("ACCOUNT TYPE:", "<span>FREE</span>"));

        Assert.Equal(new AccountSummary(9, "FREE", null, null), summary);
    }

    [Fact]
    public void Summary_text_is_stripped_of_control_characters_and_capped()
    {
        var value = $"<span>LE\u0007GEND</span><span>{new string('x', 500)}</span>";

        var summary = SummaryOf(Row("Account Type:", value))!;

        Assert.Equal("LEGEND", summary.AccountType);
        Assert.Equal(200, summary.MembershipStatus!.Length);
    }

    [Fact]
    public void Summary_rows_outside_the_account_info_card_are_ignored()
    {
        var html = Card() + "<div><div class=\"tblHeader\"><h4>Other</h4></div>" + CoinsRow + "</div>";

        var result = _parser.Parse(Snapshot(html));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Null(result.Data!.Summary);
    }

    [Fact]
    public void Summary_fields_do_not_change_the_outcome_of_the_name_parse()
    {
        var withSummary = _parser.Parse(Snapshot(Card(CoinsRow)));
        var without = _parser.Parse(Snapshot(Card()));

        Assert.Equal(without.Outcome, withSummary.Outcome);
        Assert.Equal(without.Diagnostics, withSummary.Diagnostics);
    }

    [Fact]
    public void A_page_without_a_name_fails_even_when_the_summary_is_present()
    {
        var html = "<div><div class=\"tblHeader\"><h4>Account Info</h4></div>" + CoinsRow + "</div>";

        var result = _parser.Parse(Snapshot(html));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("SENTINEL-EMAIL-STATUS")]
    [InlineData("SENTINEL-DATE-CREATED")]
    [InlineData("SENTINEL-LAST-ACCESSED")]
    [InlineData("SENTINEL-AGE-GROUP")]
    [InlineData("SENTINEL-GIFT-GIVEN")]
    [InlineData("SENTINEL-GIFT-RECEIVED")]
    public void Fields_outside_the_allow_list_never_appear_in_the_result(string sentinel)
    {
        var result = _parser.Parse(Fixture("account-home-sample.html"));

        Assert.NotNull(result.Data!.Summary);
        Assert.DoesNotContain(sentinel, result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sentinel, result.Data.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sentinel, result.Data.Summary.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sentinel, result.Diagnostics.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Name%20With%20%27Quote%27%20%26%20Spaces", "Name With 'Quote' & Spaces")]
    [InlineData("Gabe%20the%20babe", "Gabe the babe")]
    [InlineData("H%C3%A9ro%C3%AF", "Héroï")]
    [InlineData("Plus+Sign", "Plus Sign")]
    public void Url_decodes_the_link_id(string encoded, string expected)
    {
        var html = $"""<a href="https://account.aq.com/CharPage?id={encoded}">Character Page</a>""";

        var result = _parser.Parse(Snapshot(html));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(expected, result.Data!.CharacterName);
    }

    [Fact]
    public void Accepts_a_relative_character_page_link_and_extra_query_values()
    {
        var html = """<a href="/CharPage?tab=1&amp;id=Test%20Hero">Character Page</a>""";

        var result = _parser.Parse(Snapshot(html));

        Assert.Equal("Test Hero", result.Data!.CharacterName);
    }

    [Fact]
    public void Ignores_character_page_links_on_other_hosts()
    {
        var html = """<a href="https://evil.example.com/CharPage?id=Someone%20Else">Character Page</a>""";

        var result = _parser.Parse(Snapshot(html));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
    }

    [Theory]
    [InlineData("""<a href="https://account.aq.com/CharPage?id=">Character Page</a>""")]
    [InlineData("""<a href="https://account.aq.com/CharPage?id=%20%20">Character Page</a>""")]
    [InlineData("""<div class="tblHeader"><h4>Account Info</h4></div><div class="row"><div class="fw-bold">Username:</div><div class="col">   </div></div>""")]
    public void Empty_names_are_treated_as_not_found(string html)
    {
        var result = _parser.Parse(Snapshot(html));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Drifted_layout_fails_with_both_missing_markers()
    {
        var result = _parser.Parse(Fixture("account-home-drifted.html"));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
        Assert.Null(result.Diagnostics.MatchedStrategy);
        Assert.Equal(0, result.Diagnostics.ItemCount);
        Assert.Equal(
            [AccountHomeParser.CharacterPageLinkMarker, AccountHomeParser.UsernameRowMarker],
            result.Diagnostics.MissingMarkers);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void Login_page_fails_without_throwing()
    {
        var result = _parser.Parse(Fixture("account-login.html"));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
    }

    [Theory]
    [InlineData("account-home-sample.html")]
    [InlineData("account-home-username-only.html")]
    [InlineData("account-home-drifted.html")]
    public void Email_never_appears_in_the_result(string fixture)
    {
        var result = _parser.Parse(Fixture(fixture));

        Assert.DoesNotContain(FakeEmail, result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeEmail, result.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeEmail, string.Join("|", result.Diagnostics.MissingMarkers), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", result.Data?.CharacterName ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<<<>>><div class=\"fw-bold\">Username:")]
    [InlineData("<a href=\"https://account.aq.com/CharPage?id=%E0%A4%A\">Character Page</a>")]
    [InlineData("<a href=\"http://[::1\">Character Page</a><a href=\"::::\">x</a>")]
    [InlineData("\0\0<html><body><table><tr><td>")]
    public void Malformed_html_does_not_throw(string html)
    {
        var error = Record.Exception(() => _parser.Parse(Snapshot(html)));

        Assert.Null(error);
    }

    [Theory]
    [InlineData("https://account.aq.com/Home", true)]
    [InlineData("https://account.aq.com/HOME?x=1", true)]
    [InlineData("https://account.aq.com/Login", false)]
    [InlineData("https://account.aq.com/Home/Settings", false)]
    [InlineData("https://other.example.com/Home", false)]
    public void Can_parse_only_the_account_home_page(string url, bool expected)
    {
        Assert.Equal(expected, _parser.CanParse(Snapshot("<html/>", url)));
    }
}
