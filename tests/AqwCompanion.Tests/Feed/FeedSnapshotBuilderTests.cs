using System.Text.Json;
using AqwCompanion.App.Feed;
using AqwCompanion.App.GameData;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Feed;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.Feed;

public class FeedSnapshotBuilderTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static byte[] GoldExp(int gold, int exp) =>
        PlainJson(Envelope("addGoldExp", new Dictionary<string, object?> { ["intGold"] = gold, ["intExp"] = exp, ["typ"] = "m", ["id"] = 4 }));

    private static async Task<MainViewModelBuilder.Built> Captured(params IEnumerable<TcpSegment>[] connections)
    {
        var rig = new CaptureRig { Source = CaptureRig.Feeding(connections.SelectMany(c => c)) };
        var built = MainViewModelBuilder.Create(rig);
        await built.Main.GameData.StartCommand.ExecuteAsync(null).WaitAsync(Wait);
        return built;
    }

    private static FeedSnapshot Snapshot(MainViewModelBuilder.Built built) =>
        FeedSnapshotBuilder.Build(built.Rig.Session, built.Main.GameData, built.Main.HardFarms);

    [Fact]
    public void Nothing_captured_gives_the_capture_state_and_nothing_else()
    {
        var built = MainViewModelBuilder.Create();

        var snapshot = Snapshot(built);

        Assert.Equal(1, snapshot.Contract);
        Assert.Equal(GameDataViewModel.IdleText, snapshot.Capture.State);
        Assert.False(snapshot.Capture.Listening);
        Assert.Null(snapshot.Selected);
        Assert.Empty(snapshot.Characters);
        Assert.Empty(snapshot.Factions);
        Assert.Null(snapshot.HardFarms);
    }

    [Fact]
    public async Task Each_captured_character_is_listed_by_name_with_its_numbers()
    {
        var built = await Captured(
            Connection(clientPort: 50001, name: "Second Hero").Server, Connection(clientPort: 50000, name: "First Hero").Server);

        var snapshot = Snapshot(built);

        Assert.Equal(["First Hero", "Second Hero"], snapshot.Characters.Select(c => c.Name));
        var first = snapshot.Characters[0];
        Assert.Equal(60, first.Level);
        Assert.Equal(new FeedFigures(9876543210L, 123456, 654321, 777, 5000, 300), first.Figures);
        Assert.False(first.Live);
        Assert.Equal(0, first.LiveUpdates);
        Assert.Null(first.LastLiveUpdateUtc);
        Assert.Null(first.LastKill);
        Assert.Equal(built.Main.GameData.SelectedCharacter, snapshot.Selected);
    }

    [Fact]
    public async Task The_factions_of_the_selected_character_are_ordered_by_what_is_missing_with_max_ranks_last()
    {
        var built = await Captured(Connection().Server);

        var factions = Snapshot(built).Factions;

        Assert.Equal(["Fresh Order", "Fictional Order", "Other Order"], factions.Select(f => f.Name));
        Assert.Equal(new FeedFaction("Fresh Order", 1, 0, 900, 0, false), factions[0]);
        Assert.Equal(900, factions[0].ToNext);
        Assert.Equal(5000, factions[1].ToNext);
        Assert.Equal(1400d / 6400d, factions[1].Fraction, 6);
        Assert.True(factions[2].Max);
        Assert.Equal(0, factions[2].ToNext);
        Assert.Equal(1, factions[2].Fraction);
    }

    [Fact]
    public async Task The_last_kill_is_carried_by_exactly_one_live_character()
    {
        var alpha = Connection(clientPort: 50000, name: "Alpha Hero");
        alpha.Server.Add(ServerData(alpha.NextServerSequence, [.. GoldExp(10, 1), 0], 50000));
        var beta = Connection(clientPort: 50001, name: "Beta Hero");
        beta.Server.Add(ServerData(beta.NextServerSequence, [.. GoldExp(1200, 340), 0], 50001));
        var built = await Captured(alpha.Server, beta.Server);

        var snapshot = Snapshot(built);

        var byName = snapshot.Characters.ToDictionary(c => c.Name);
        Assert.True(byName["Alpha Hero"].Live);
        Assert.True(byName["Beta Hero"].Live);
        Assert.Equal(1, byName["Alpha Hero"].LiveUpdates);
        Assert.NotNull(byName["Beta Hero"].LastLiveUpdateUtc);
        // Exactly one character carries the session's last kill (the one updated last; the clock may tie, so which is not asserted).
        var carrier = Assert.Single(snapshot.Characters, c => c.LastKill is not null);
        Assert.Equal(new FeedKill(1200, 340), carrier.LastKill);
    }

    [Fact]
    public async Task Hard_farms_count_what_the_selected_character_owns_and_name_what_it_does_not()
    {
        var built = await Captured(Connection().Server);
        built.Store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one", "cape-one", "pet-one"])], null), DateTimeOffset.UnixEpoch);
        built.Inventory.Data = new CharacterInventory([new InventoryItem("Blade One", "Sword", 1, false, false)]);
        await built.Main.HardFarms.RestoreAsync();

        var hardFarms = Snapshot(built).HardFarms;

        Assert.NotNull(hardFarms);
        Assert.Equal(1, hardFarms.Owned);
        Assert.Equal(3, hardFarms.Total);
        Assert.Equal(["Cape One", "Pet One"], hardFarms.Missing);
    }

    [Fact]
    public async Task Hard_farms_are_left_out_while_the_ownership_is_unknown()
    {
        var built = await Captured(Connection().Server);
        built.Store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), DateTimeOffset.UnixEpoch);
        await built.Main.HardFarms.RestoreAsync();

        Assert.Null(Snapshot(built).HardFarms);
    }

    [Fact]
    public async Task The_json_carries_none_of_the_secrets_the_game_sends()
    {
        var built = await Captured(Connection().Server);

        var json = FeedJson.Serialize(Snapshot(built));

        Capture.SecretScan.AssertNoSecrets(json);
        using var document = JsonDocument.Parse(json);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, document.RootElement.GetProperty("contract").GetInt32());
    }

    [Fact]
    public async Task The_source_serves_a_cached_snapshot_until_something_changes_and_then_a_new_one()
    {
        var built = await Captured(Connection().Server);
        using var source = new SessionFeedSource(built.Rig.Session, built.Main.GameData, built.Main.HardFarms);
        var changes = 0;
        source.Changed += (_, _) => changes++;

        var first = source.Current;
        Assert.Same(first, source.Current);

        built.Inventory.Data = new CharacterInventory([]);
        built.Store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), DateTimeOffset.UnixEpoch);
        await built.Main.HardFarms.RestoreAsync();

        Assert.True(changes >= 1);
        Assert.NotSame(first, source.Current);
        Assert.NotNull(source.Current.HardFarms);
    }

    [Fact]
    public async Task A_disposed_source_stops_listening()
    {
        var built = await Captured(Connection().Server);
        var source = new SessionFeedSource(built.Rig.Session, built.Main.GameData, built.Main.HardFarms);
        var changes = 0;
        source.Changed += (_, _) => changes++;
        source.Dispose();

        built.Inventory.Data = new CharacterInventory([]);

        Assert.Equal(0, changes);
    }
}
