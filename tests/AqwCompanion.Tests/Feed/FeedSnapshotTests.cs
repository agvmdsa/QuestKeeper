using System.Text.Json.Nodes;
using AqwCompanion.Core.Feed;

namespace AqwCompanion.Tests.Feed;

public class FeedSnapshotTests
{
    private static readonly DateTimeOffset Saved = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static FeedSnapshot Sample() => new(
        FeedSnapshot.ContractNumber,
        new FeedCapture("Waiting for a login. Open the game launcher now.", true),
        "Alpha Hero",
        [
            new FeedCharacter(
                "Alpha Hero", 60, true, Saved, Saved.AddMinutes(5), 3,
                new FeedFigures(1200, 340, 5000, 777, 99, 300), new FeedKill(56, 0)),
            new FeedCharacter(
                "Beta Hero", 12, false, Saved.AddHours(-1), null, 0,
                new FeedFigures(5, 6, 7, 8, 9, 10), null),
        ],
        [new FeedFaction("Nation", 3, 5000, 5000, 1400d / 6400d, false)],
        new FeedHardFarms(4, 8, ["Cape of Chaos", "Nation Hoodie"]));

    [Fact]
    public void The_json_is_the_documented_contract()
    {
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Feed", "snapshot-contract-1.json")))!;

        var actual = JsonNode.Parse(FeedJson.Serialize(Sample()))!;

        Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
    }

    [Fact]
    public void The_empty_snapshot_is_contract_one_with_nothing_in_it()
    {
        var empty = FeedSnapshot.Empty;

        Assert.Equal(1, empty.Contract);
        Assert.Null(empty.Selected);
        Assert.Empty(empty.Characters);
        Assert.Empty(empty.Factions);
        Assert.Null(empty.HardFarms);
        Assert.False(empty.Capture.Listening);
        Assert.Equal("""{"contract":1,"capture":{"state":"Not capturing.","listening":false},"selected":null,"characters":[],"factions":[],"hardFarms":null}""", FeedJson.Serialize(empty));
    }

    [Fact]
    public void The_missing_names_are_cut_to_two_hundred()
    {
        var names = Enumerable.Range(0, 250).Select(i => "Item " + i).ToList();

        var capped = FeedHardFarms.Cap(names);

        Assert.Equal(200, capped.Count);
        Assert.Equal("Item 0", capped[0]);
        Assert.Equal("Item 199", capped[^1]);
    }

    [Fact]
    public void A_short_list_is_kept_whole()
    {
        Assert.Equal(["A", "B"], FeedHardFarms.Cap(["A", "B"]));
    }
}
