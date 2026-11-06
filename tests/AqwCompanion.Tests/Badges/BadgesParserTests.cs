using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.Badges;

public class BadgesParserTests
{
    private readonly BadgesParser _parser = new();

    private static PageSnapshot Snapshot(string body, string url = "https://account.aq.com/CharPage/Badges?ccid=3") =>
        PageSnapshot.Create(url, body, "1.0.0", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Reads_badges_of_two_categories_and_ignores_the_image_name()
    {
        const string body = """
            [
              {"sTitle":"Test Badge A","sDesc":"First test badge.","sCategory":"Quests","sFileName":"a.png"},
              {"sTitle":"Test Badge B","sDesc":"Second test badge.","sCategory":"Events","sFileName":"b.png"},
              {"sTitle":"Test Badge C","sDesc":"Third test badge.","sCategory":"quests","sFileName":"c.png"}
            ]
            """;

        var result = _parser.Parse(Snapshot(body));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        var badges = result.Data!;
        Assert.Equal(3, badges.Count);
        Assert.Equal(new Badge("Test Badge A", "First test badge.", "Quests"), badges.Badges[0]);
        Assert.Equal("json-array", result.Diagnostics.MatchedStrategy);
        Assert.Equal("character-badges", result.Diagnostics.ParserName);
        Assert.Equal(3, result.Diagnostics.ItemCount);
        Assert.Empty(result.Diagnostics.MissingMarkers);

        var groups = badges.GroupedByCategory;
        Assert.Equal(["Quests", "Events"], groups.Select(g => g.Category));
        Assert.Equal(["Test Badge A", "Test Badge C"], groups[0].Badges.Select(b => b.Title));
        Assert.Equal(["Test Badge B"], groups[1].Badges.Select(b => b.Title));
    }

    [Fact]
    public void Preview_is_the_first_five_badges()
    {
        var body = "[" + string.Join(",", Enumerable.Range(1, 7).Select(i => $$"""{"sTitle":"Badge {{i}}","sDesc":"d","sCategory":"c"}""")) + "]";

        var badges = _parser.Parse(Snapshot(body)).Data!;

        Assert.Equal(7, badges.Count);
        Assert.Equal(["Badge 1", "Badge 2", "Badge 3", "Badge 4", "Badge 5"], badges.Preview.Select(b => b.Title));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("  Error\n")]
    [InlineData("")]
    public void An_error_or_empty_body_fails(string body)
    {
        var result = _parser.Parse(Snapshot(body));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
        Assert.NotEmpty(result.Diagnostics.MissingMarkers);
        Assert.Equal(0, result.Diagnostics.ItemCount);
    }

    [Theory]
    [InlineData("[{\"sTitle\":\"Test")]
    [InlineData("<html>not json</html>")]
    public void Malformed_json_fails(string body)
    {
        var result = _parser.Parse(Snapshot(body));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("{\"sTitle\":\"Test Badge\"}")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void Json_that_is_not_an_array_fails(string body)
    {
        var result = _parser.Parse(Snapshot(body));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
    }

    [Fact]
    public void An_empty_array_is_ok_with_zero_badges()
    {
        var result = _parser.Parse(Snapshot("[]"));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(0, result.Data!.Count);
        Assert.Empty(result.Data.GroupedByCategory);
        Assert.Equal(0, result.Diagnostics.ItemCount);
    }

    [Fact]
    public void Missing_fields_fall_back_and_entries_without_a_title_are_skipped()
    {
        const string body = """
            [
              {"sTitle":"Only Title"},
              {"sDesc":"No title here","sCategory":"Quests"},
              {"sTitle":"  ","sDesc":"Blank title"},
              {"sTitle":7,"sDesc":"Title is not a string"},
              "not an object",
              {"sTitle":"Blank Category","sDesc":"d","sCategory":""}
            ]
            """;

        var result = _parser.Parse(Snapshot(body));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(
            [new Badge("Only Title", string.Empty, CharacterBadges.UncategorizedName),
             new Badge("Blank Category", "d", CharacterBadges.UncategorizedName)],
            result.Data!.Badges);
        Assert.Equal(2, result.Diagnostics.ItemCount);
    }

    [Fact]
    public void A_non_empty_array_without_any_usable_entry_is_suspicious()
    {
        var result = _parser.Parse(Snapshot("""[{"sDesc":"x"}]"""));

        Assert.Equal(ParseOutcome.Suspicious, result.Outcome);
        Assert.Equal(0, result.Data!.Count);
        Assert.NotEmpty(result.Diagnostics.MissingMarkers);
    }

    [Fact]
    public void Control_characters_are_removed_and_long_text_is_capped()
    {
        var body = "[{\"sTitle\":\"Test\\u0007 Badge\\n\",\"sDesc\":\"" + new string('x', 900) + "\",\"sCategory\":\"c\"}]";

        var badge = _parser.Parse(Snapshot(body)).Data!.Badges[0];

        Assert.Equal("Test Badge", badge.Title);
        Assert.Equal(500, badge.Description.Length);
    }

    [Fact]
    public void Can_parse_only_the_badges_endpoint()
    {
        Assert.True(_parser.CanParse(Snapshot("[]")));
        Assert.False(_parser.CanParse(Snapshot("[]", "https://account.aq.com/CharPage?id=Test")));
        Assert.False(_parser.CanParse(Snapshot("[]", "https://account.aq.com/CharPage/Inventory?ccid=3")));
    }

    [Theory]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(201)]
    public void A_title_cut_inside_a_surrogate_pair_never_ends_on_a_lone_surrogate(int prefix)
    {
        var title = new string('a', prefix) + "\U0001F600" + "tail";
        var body = System.Text.Json.JsonSerializer.Serialize(new[] { new { sTitle = title, sDesc = "d", sCategory = "c" } });

        var badge = Assert.Single(_parser.Parse(Snapshot(body)).Data!.Badges);

        Assert.True(badge.Title.Length <= 200);
        Assert.False(char.IsHighSurrogate(badge.Title[^1]));
        var json = System.Text.Json.JsonSerializer.Serialize(badge, AqwCompanion.App.Infrastructure.Persistence.StoredJson.Options);
        Assert.Equal(badge, System.Text.Json.JsonSerializer.Deserialize<Badge>(json, AqwCompanion.App.Infrastructure.Persistence.StoredJson.Options));
    }
}
