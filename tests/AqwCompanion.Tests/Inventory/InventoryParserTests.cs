using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Site;

namespace AqwCompanion.Tests.Inventory;

public class InventoryParserTests
{
    private readonly InventoryParser _parser = new();

    private static PageSnapshot Snapshot(string body, string url = "https://account.aq.com/CharPage/Inventory?ccid=3") =>
        PageSnapshot.Create(url, body, "1.0.0", DateTimeOffset.UnixEpoch);

    private const string RealShape = """
        [
          {"intCharId":3,"strName":"Test Class","bUpgrade":true,"bCoins":false,"intCount":1122,"strType":"Class","sortOrder":1},
          {"intCharId":3,"strName":"Test Armor","bUpgrade":false,"bCoins":true,"intCount":1,"strType":"Armor","sortOrder":2},
          {"intCharId":3,"strName":"Test Sword","bUpgrade":false,"bCoins":false,"intCount":1,"strType":"Sword","sortOrder":3},
          {"intCharId":3,"strName":"Test Ore","bUpgrade":false,"bCoins":false,"intCount":250,"strType":"Resource","sortOrder":4},
          {"intCharId":3,"strName":"Test Blade","bUpgrade":false,"bCoins":false,"intCount":1,"strType":"Sword","sortOrder":5}
        ]
        """;

    [Fact]
    public void Can_parse_only_the_inventory_url()
    {
        Assert.True(_parser.CanParse(Snapshot("[]")));
        Assert.False(_parser.CanParse(Snapshot("[]", "https://account.aq.com/CharPage/Badges?ccid=3")));
        Assert.False(_parser.CanParse(Snapshot("[]", "https://evil.example.com/CharPage/Inventory?ccid=3")));
    }

    [Fact]
    public void Reads_the_real_shape()
    {
        var result = _parser.Parse(Snapshot(RealShape));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        var inventory = result.Data!;
        Assert.Equal(5, inventory.Count);
        Assert.Equal(new InventoryItem("Test Class", "Class", 1122, false, true), inventory.Items[0]);
        Assert.Equal(new InventoryItem("Test Armor", "Armor", 1, true, false), inventory.Items[1]);
        Assert.Equal(250, inventory.CountOf("Test Ore"));
        Assert.Equal("json-array", result.Diagnostics.MatchedStrategy);
        Assert.Equal("character-inventory", result.Diagnostics.ParserName);
        Assert.Equal(5, result.Diagnostics.ItemCount);
        Assert.Empty(result.Diagnostics.MissingMarkers);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("  Error\n")]
    [InlineData("")]
    [InlineData("[{\"strName\":\"Test")]
    [InlineData("<html>not json</html>")]
    [InlineData("{\"strName\":\"Test\"}")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void An_error_empty_malformed_or_non_array_body_fails(string body)
    {
        var result = _parser.Parse(Snapshot(body));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
        Assert.NotEmpty(result.Diagnostics.MissingMarkers);
        Assert.Equal(0, result.Diagnostics.ItemCount);
    }

    [Fact]
    public void An_empty_array_is_ok_with_zero_items()
    {
        var result = _parser.Parse(Snapshot("[]"));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(0, result.Data!.Count);
        Assert.Empty(result.Data.TypeCounts);
        Assert.Equal(0, result.Diagnostics.ItemCount);
    }

    [Fact]
    public void Entries_without_a_usable_name_are_skipped_and_a_missing_type_becomes_other()
    {
        const string body = """
            [
              {"strName":"Only Name"},
              {"strType":"Armor","intCount":2},
              {"strName":"  ","strType":"Armor"},
              {"strName":7,"strType":"Armor"},
              "not an object",
              {"strName":"Blank Type","strType":""}
            ]
            """;

        var result = _parser.Parse(Snapshot(body));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(
            [new InventoryItem("Only Name", "Other", 1, false, false), new InventoryItem("Blank Type", "Other", 1, false, false)],
            result.Data!.Items);
        Assert.Equal(2, result.Diagnostics.ItemCount);
    }

    [Theory]
    [InlineData("\"intCount\":-5", 1)]
    [InlineData("\"intCount\":99999999999", 1)]
    [InlineData("\"intCount\":\"7\"", 1)]
    [InlineData("\"intCount\":2.5", 1)]
    [InlineData("\"intCount\":null", 1)]
    [InlineData("\"x\":1", 1)]
    [InlineData("\"intCount\":0", 0)]
    [InlineData("\"intCount\":42", 42)]
    public void Counts_fall_back_to_one_for_items_and_keep_zero(string countJson, int expected)
    {
        var body = "[{\"strName\":\"Test Item\",\"strType\":\"Item\"," + countJson + "}]";

        var item = _parser.Parse(Snapshot(body)).Data!.Items[0];

        Assert.Equal(expected, item.Count);
    }

    [Theory]
    [InlineData("\"intCount\":-5")]
    [InlineData("\"intCount\":99999999999")]
    [InlineData("\"x\":1")]
    public void A_class_without_a_usable_count_has_zero_points(string countJson)
    {
        var body = "[{\"strName\":\"Test Class\",\"strType\":\"Class\"," + countJson + "}]";

        Assert.Equal(0, _parser.Parse(Snapshot(body)).Data!.Items[0].Count);
    }

    [Fact]
    public void A_non_empty_array_without_any_usable_entry_is_suspicious()
    {
        var result = _parser.Parse(Snapshot("""[{"strType":"Armor"},{"intCount":3}]"""));

        Assert.Equal(ParseOutcome.Suspicious, result.Outcome);
        Assert.Equal(0, result.Data!.Count);
        Assert.NotEmpty(result.Diagnostics.MissingMarkers);
    }

    [Fact]
    public void Control_characters_are_removed_and_long_text_is_capped()
    {
        var body = "[{\"strName\":\"Test\\u0007 Item\\n\",\"strType\":\"" + new string('t', 400) + "\"}," +
                   "{\"strName\":\"" + new string('x', 900) + "\",\"strType\":\"Item\"}]";

        var items = _parser.Parse(Snapshot(body)).Data!.Items;

        Assert.Equal("Test Item", items[0].Name);
        Assert.Equal(new string('t', 100), items[0].Type);
        Assert.Equal(new string('x', 200), items[1].Name);
    }

    [Fact]
    public void Count_of_and_has_ignore_case_and_whitespace_and_sum_duplicates()
    {
        const string body = """
            [
              {"strName":"Test  Ore","strType":"Resource","intCount":5},
              {"strName":"test ore","strType":"Item","intCount":3},
              {"strName":"Other","strType":"Item","intCount":9}
            ]
            """;
        var inventory = _parser.Parse(Snapshot(body)).Data!;

        Assert.Equal(8, inventory.CountOf("  TEST ORE "));
        Assert.True(inventory.Has("test\tore"));
        Assert.Equal(0, inventory.CountOf("Missing"));
        Assert.False(inventory.Has("Missing"));
        Assert.False(inventory.Has("   "));
        Assert.Equal("Other", inventory.Find("OTHER")!.Name);
        Assert.Null(inventory.Find("Missing"));
    }

    [Fact]
    public void Type_counts_are_ordered_by_count_then_name()
    {
        var inventory = _parser.Parse(Snapshot(RealShape)).Data!;

        Assert.Equal(
            [new InventoryTypeCount("Sword", 2), new InventoryTypeCount("Armor", 1), new InventoryTypeCount("Class", 1), new InventoryTypeCount("Resource", 1)],
            inventory.TypeCounts);
    }

    [Theory]
    [InlineData("  Hello   World ", "hello world")]
    [InlineData("A\tB\nC", "a b c")]
    [InlineData("", "")]
    public void Name_key_trims_collapses_whitespace_and_ignores_case(string text, string expected)
    {
        Assert.Equal(expected, NameKey.Normalize(text).ToLowerInvariant());
        Assert.Equal(NameKey.Normalize(text), NameKey.Normalize(text.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Urls_address_the_inventory_by_character_id()
    {
        Assert.Equal("https://account.aq.com/CharPage/Inventory?ccid=3", AqwUrls.Inventory(3));
        Assert.True(AqwUrls.IsInventory(AqwUrls.Inventory(123456)));
        Assert.False(AqwUrls.IsInventory("https://account.aq.com/CharPage/Badges?ccid=3"));
    }

    [Theory]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(201)]
    public void A_name_cut_inside_a_surrogate_pair_never_ends_on_a_lone_surrogate(int prefix)
    {
        var name = new string('a', prefix) + "\U0001F600" + "tail";
        var body = System.Text.Json.JsonSerializer.Serialize(new[] { new { strName = name, strType = "Sword", intCount = 1 } });

        var item = Assert.Single(_parser.Parse(Snapshot(body)).Data!.Items);

        Assert.True(item.Name.Length <= 200);
        Assert.False(char.IsHighSurrogate(item.Name[^1]));
        var json = System.Text.Json.JsonSerializer.Serialize(item, StoredJson.Options);
        Assert.Equal(item, System.Text.Json.JsonSerializer.Deserialize<InventoryItem>(json, StoredJson.Options));
    }
}
