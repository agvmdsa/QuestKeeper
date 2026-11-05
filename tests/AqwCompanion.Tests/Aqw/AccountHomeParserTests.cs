using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;

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
        Assert.Equal(new AccountIdentity("Test Hero"), result.Data);
        Assert.Equal("character-page-link", result.Diagnostics.MatchedStrategy);
        Assert.Equal("account-home", result.Diagnostics.ParserName);
        Assert.Equal("1", result.Diagnostics.ParserVersion);
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
