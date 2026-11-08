using System.Text.Json;
using AqwCompanion.Core.Capture;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;
using static AqwCompanion.Tests.Capture.SecretScan;

namespace AqwCompanion.Tests.Capture;

public class CaptureEngineLiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>One engine with the events it raises, in order.</summary>
    private sealed class Rig
    {
        public CaptureEngine Engine { get; } = new(new FixedClock());

        public List<CharacterSnapshot> Captured { get; } = [];

        public List<CharacterSnapshot> Updated { get; } = [];

        public List<string> Order { get; } = [];

        public Rig()
        {
            Engine.CharacterCaptured += snapshot =>
            {
                Captured.Add(snapshot);
                Order.Add("captured");
            };
            Engine.CharacterUpdated += snapshot =>
            {
                Updated.Add(snapshot);
                Order.Add("updated");
            };
        }

        /// <summary>Runs a full login on a client port and returns the handle to keep talking on that connection.</summary>
        public Line Login(int clientPort = ClientPort, string name = "Fictional Hero", uint isn = 1000)
        {
            var traffic = Connection(clientPort: clientPort, name: name, isn: isn);
            foreach (var segment in traffic.All)
            {
                Engine.Accept(segment);
            }

            return new Line(this, clientPort, traffic.NextServerSequence);
        }
    }

    /// <summary>One connection after its login: sends server messages one segment at a time.</summary>
    private sealed class Line(Rig rig, int clientPort, uint sequence)
    {
        public uint Sequence { get; private set; } = sequence;

        public int ClientPort { get; } = clientPort;

        public void Send(params byte[][] messages)
        {
            var payload = messages.SelectMany(m => m.Append((byte)0)).ToArray();
            rig.Engine.Accept(ServerData(Sequence, payload, ClientPort));
            Sequence += (uint)payload.Length;
        }

        public void Fin() => rig.Engine.Accept(Closing(Sequence, ClientPort, rst: false));

        public void Rst() => rig.Engine.Accept(Closing(Sequence, ClientPort, rst: true));
    }

    private static TcpSegment Closing(uint sequence, int clientPort, bool rst) =>
        new(PcapBuilder.ServerAddress, GamePort, PcapBuilder.ClientAddress, clientPort, sequence, false, [], IsFin: !rst, IsRst: rst);

    private static Dictionary<string, object?> Body(params (string Key, object? Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    private static byte[] GoldExp(object gold, object exp) =>
        PlainJson(Envelope("addGoldExp", Body(("intGold", gold), ("intExp", exp), ("typ", "m"), ("id", 4))));

    private static byte[] Plain(string cmd) => PlainJson(Envelope(cmd, Body(("id", 1))));

    [Fact]
    public void The_live_diagnostics_start_at_zero()
    {
        var rig = new Rig();

        Assert.Equal(CaptureLiveDiagnostics.Empty, rig.Engine.Live);
        Assert.Equal(new CaptureLiveDiagnostics(0, 0, 0, 0, 0, 0, 0), rig.Engine.Live);
    }

    [Fact]
    public void A_plain_gold_event_after_the_login_raises_character_updated_with_the_gold_increased()
    {
        var rig = new Rig();
        var line = rig.Login();

        line.Send(GoldExp(40, 15));

        var updated = Assert.Single(rig.Updated);
        Assert.Equal(9876543210L + 40, updated.Profile.Gold);
        Assert.Equal(123456L + 15, updated.Profile.Exp);
        Assert.Equal("Fictional Hero", updated.Profile.Name);
        var captured = Assert.Single(rig.Captured);
        Assert.Equal(9876543210L, captured.Profile.Gold);
        Assert.Equal(["captured", "updated"], rig.Order);
        var live = rig.Engine.Live;
        Assert.Equal((1, 1, 0, 0, 40L, 15L), (live.LiveConnections, live.LiveEventsApplied, live.LiveEventsIgnored, live.LiveEventsUnmatched, live.LastGoldGain, live.LastExpGain));
    }

    [Fact]
    public void Events_pile_up_on_the_newest_snapshot_of_the_connection()
    {
        var rig = new Rig();
        var line = rig.Login();

        line.Send(GoldExp(40, 15));
        line.Send(GoldExp(10, 5), PlainJson(Envelope("updateClass", Body(("iCP", 777)))));

        Assert.Equal(3, rig.Updated.Count);
        Assert.Equal(9876543210L + 50, rig.Updated[^1].Profile.Gold);
        Assert.Equal(777, rig.Updated[^1].Profile.ClassPoints);
        Assert.Equal((10L, 5L), (rig.Engine.Live.LastGoldGain, rig.Engine.Live.LastExpGain));
    }

    [Fact]
    public void A_live_event_in_the_same_segment_as_the_end_of_the_login_is_applied_after_the_capture()
    {
        var rig = new Rig();

        foreach (var segment in Connection(segmentSize: 1_000_000, extraServerMessages: [GoldExp(7, 3)]).All)
        {
            rig.Engine.Accept(segment);
        }

        Assert.Equal(["captured", "updated"], rig.Order);
        Assert.Equal(9876543210L + 7, Assert.Single(rig.Updated).Profile.Gold);
    }

    [Fact]
    public void The_same_event_inside_an_encoded_blob_is_never_decoded()
    {
        var rig = new Rig();
        var line = rig.Login();
        var decodedBefore = rig.Engine.Counters.MessagesDecoded;
        var skippedBefore = rig.Engine.Live.LiveEncodedSkipped;
        var failuresBefore = rig.Engine.Decoding.Failures.Values.Sum();

        line.Send(EncodeBlob(Envelope("addGoldExp", Body(("intGold", 500), ("intExp", 500), ("typ", "m")))));

        Assert.Empty(rig.Updated);
        Assert.Equal(decodedBefore, rig.Engine.Counters.MessagesDecoded);
        Assert.Equal(skippedBefore + 1, rig.Engine.Live.LiveEncodedSkipped);
        Assert.Equal(failuresBefore, rig.Engine.Decoding.Failures.Values.Sum());
        Assert.Equal(0, rig.Engine.Decoding.Command("addGoldExp"));
    }

    [Fact]
    public void Other_players_profile_messages_after_the_login_are_never_decoded()
    {
        var rig = new Rig();
        var line = rig.Login();

        line.Send(
            EncodeBlob(Envelope("initUserDatas", Body(("a", new List<object?> { new Dictionary<string, object?> { ["data"] = ProfileData(SecretPlayer, own: false) } })))),
            EncodeBlob(InitUserData(ProfileData(SecretPlayer + "-3"))),
            EncodeBlob(StandardInventory()));

        Assert.Single(rig.Captured);
        Assert.Empty(rig.Updated);
        Assert.Equal(2, rig.Engine.Counters.MessagesDecoded);
        Assert.Equal(5, rig.Engine.Live.LiveEncodedSkipped);
        Assert.Equal(0, rig.Engine.Decoding.Command("initUserDatas"));
        Assert.Equal(1, rig.Engine.Decoding.Command("initUserData"));
        Assert.Equal(1, rig.Engine.Decoding.InitUserDataAccepted);
        Assert.Equal((1, 1), (rig.Engine.Stages.ProfilePartsSeen, rig.Engine.Stages.InventoryPartsSeen));
    }

    [Fact]
    public void Text_and_xml_messages_on_a_live_connection_are_skipped_without_reading()
    {
        var rig = new Rig();
        var line = rig.Login();
        var before = rig.Engine.Live.LiveEncodedSkipped;

        line.Send(Bytes("%xt%secret%1%" + SecretToken + "%"), Bytes("<msg t='sys'>" + SecretPlayer + "</msg>"));

        Assert.Equal(before + 2, rig.Engine.Live.LiveEncodedSkipped);
        Assert.Equal(2, rig.Engine.Counters.MessagesDecoded);
        AssertNoSecrets(rig.Engine.Live);
        AssertNoSecrets(rig.Engine.Decoding);
    }

    [Fact]
    public void An_unknown_plain_command_is_counted_by_name_without_parsing_the_json()
    {
        var rig = new Rig();
        var line = rig.Login();
        var brokenUnknown = Bytes("{\"t\":\"xt\",\"b\":{\"r\":-1,\"o\":{\"cmd\":\"zzz\",\"x\":[1,2,}}}" + SecretPlayer);
        var brokenKnown = Bytes("{\"t\":\"xt\",\"b\":{\"r\":-1,\"o\":{\"cmd\":\"ct\",\"p\":{\"" + SecretPlayer + "\":}}}");

        var thrown = Record.Exception(() => line.Send(brokenUnknown, brokenUnknown, brokenKnown));

        Assert.Null(thrown);
        var decoding = rig.Engine.Decoding;
        Assert.Equal([KeyValuePair.Create("zzz", 2)], decoding.OtherNames);
        Assert.Equal(2, decoding.Command("other"));
        Assert.Equal(1, decoding.Command("ct"));
        Assert.Equal(0, decoding.Failure(DecodeFailure.InvalidJson));
        Assert.Equal(2, rig.Engine.Counters.MessagesDecoded);
        Assert.Empty(rig.Updated);
        AssertNoSecrets(decoding);
    }

    [Fact]
    public void A_live_command_with_broken_json_is_ignored_without_an_exception()
    {
        var rig = new Rig();
        var line = rig.Login();

        var thrown = Record.Exception(() => line.Send(Bytes("{\"t\":\"xt\",\"b\":{\"r\":-1,\"o\":{\"cmd\":\"addGoldExp\",\"intGold\":}}}")));

        Assert.Null(thrown);
        Assert.Empty(rig.Updated);
        Assert.Equal(1, rig.Engine.Live.LiveEventsIgnored);
    }

    [Fact]
    public void The_command_name_is_only_looked_for_in_the_first_256_bytes()
    {
        var rig = new Rig();
        var line = rig.Login();
        var late = Bytes("{\"t\":\"xt\",\"pad\":\"" + new string('a', 300) + "\",\"b\":{\"o\":{\"cmd\":\"addGoldExp\",\"intGold\":900,\"intExp\":900}}}");
        var early = Bytes("{\"t\":\"xt\",\"b\":{\"o\":{\"cmd\":\"addGoldExp\",\"intGold\":900,\"intExp\":900}},\"pad\":\"" + new string('a', 300) + "\"}");

        line.Send(late);
        Assert.Empty(rig.Updated);
        Assert.Equal(1, rig.Engine.Decoding.Command("other"));

        line.Send(early);
        Assert.Single(rig.Updated);
    }

    [Theory]
    [InlineData("add Gold")]
    [InlineData("addGold\\nExp")]
    [InlineData("")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789_+.-X")]
    [InlineData("add\\\"GoldExp")]
    public void Command_names_outside_the_restricted_charset_or_length_are_not_live_and_not_listed(string name)
    {
        var rig = new Rig();
        var line = rig.Login();

        line.Send(Bytes("{\"t\":\"xt\",\"b\":{\"o\":{\"cmd\":\"" + name + "\",\"intGold\":900,\"intExp\":900}}}"));

        Assert.Empty(rig.Updated);
        Assert.Empty(rig.Engine.Decoding.OtherNames);
        Assert.Equal(1, rig.Engine.Decoding.Command("other"));
    }

    [Fact]
    public void The_scan_reads_the_name_from_the_first_bytes_and_nothing_else()
    {
        Assert.Equal("addGoldExp", LiveCommandScan.ReadName(Bytes("{\"t\":\"xt\",\"b\":{\"o\":{\"cmd\":\"addGoldExp\"}}}")));
        Assert.Equal("rep+gain.v2", LiveCommandScan.ReadName(Bytes("{\"cmd\":\"rep+gain.v2\"")));
        Assert.Null(LiveCommandScan.ReadName(Bytes("{\"cmd\":\"")));
        Assert.Null(LiveCommandScan.ReadName(Bytes("{\"cmd\":\"unterminated")));
        Assert.Null(LiveCommandScan.ReadName(Bytes("{\"cmd\": \"spaced\"}")));
        Assert.Null(LiveCommandScan.ReadName(Bytes("{\"command\":\"x\"}")));
        Assert.Null(LiveCommandScan.ReadName([]));
        Assert.Equal(256, LiveCommandScan.MaxScanBytes);
        Assert.Equal(40, LiveCommandScan.MaxNameLength);
    }

    [Fact]
    public void Client_to_server_segments_on_a_live_connection_are_still_dropped_first()
    {
        var rig = new Rig();
        var line = rig.Login();
        var before = rig.Engine.Counters;

        rig.Engine.Accept(ClientData(9000, [.. GoldExp(999, 999), 0], line.ClientPort));
        rig.Engine.Accept(ClientData(9100, [.. EncodeBlob(InitUserData()), 0], line.ClientPort));

        Assert.Empty(rig.Updated);
        Assert.Equal(before.SegmentsDropped + 2, rig.Engine.Counters.SegmentsDropped);
        Assert.Equal(before.MessagesDecoded, rig.Engine.Counters.MessagesDecoded);
        Assert.Equal(1, rig.Engine.Live.LiveConnections);
    }

    [Fact]
    public void A_fin_frees_the_live_connection_and_later_events_on_it_are_dropped()
    {
        var rig = new Rig();
        var line = rig.Login();

        line.Fin();
        line.Send(GoldExp(40, 15));

        Assert.Equal(0, rig.Engine.Live.LiveConnections);
        Assert.Empty(rig.Updated);
    }

    [Fact]
    public void A_rst_frees_the_live_connection_too()
    {
        var rig = new Rig();
        var line = rig.Login();

        line.Rst();

        Assert.Equal(0, rig.Engine.Live.LiveConnections);
    }

    [Fact]
    public void A_fin_that_carries_data_still_delivers_it_before_the_connection_is_freed()
    {
        var rig = new Rig();
        var line = rig.Login();
        byte[] payload = [.. GoldExp(40, 15), 0];

        rig.Engine.Accept(new TcpSegment(PcapBuilder.ServerAddress, GamePort, PcapBuilder.ClientAddress, line.ClientPort, line.Sequence, false, payload, IsFin: true));

        Assert.Single(rig.Updated);
        Assert.Equal(0, rig.Engine.Live.LiveConnections);
    }

    [Fact]
    public void A_second_login_on_the_same_address_pair_after_a_new_syn_ack_works()
    {
        var rig = new Rig();
        var first = rig.Login(name: "First Hero");
        first.Fin();

        var second = rig.Login(name: "Second Hero", isn: 90000);
        second.Send(GoldExp(40, 15));

        Assert.Equal(["First Hero", "Second Hero"], rig.Captured.Select(c => c.Profile.Name));
        Assert.Equal("Second Hero", Assert.Single(rig.Updated).Profile.Name);
        Assert.Equal(1, rig.Engine.Live.LiveConnections);
    }

    [Fact]
    public void A_new_syn_ack_on_a_live_key_replaces_the_old_state_even_without_a_fin()
    {
        var rig = new Rig();
        var line = rig.Login();

        rig.Engine.Accept(SynAck(70000));
        line.Send(GoldExp(40, 15));

        Assert.Empty(rig.Updated);
        Assert.Equal(0, rig.Engine.Live.LiveConnections);

        // The replaced connection logs in again like a new one.
        foreach (var segment in Connection(isn: 70000, name: "Again Hero").Server)
        {
            rig.Engine.Accept(segment);
        }

        Assert.Equal(1, rig.Engine.Live.LiveConnections);
        Assert.Equal("Again Hero", rig.Captured[^1].Profile.Name);
    }

    [Fact]
    public void Two_live_connections_update_their_own_characters_only()
    {
        var rig = new Rig();
        var first = rig.Login(50001, "First Hero");
        var second = rig.Login(50002, "Second Hero", isn: 77000);

        first.Send(GoldExp(10, 1));
        second.Send(GoldExp(1000, 100));
        first.Send(GoldExp(10, 1));

        Assert.Equal(2, rig.Engine.Live.LiveConnections);
        Assert.Equal(
            [("First Hero", 9876543220L), ("Second Hero", 9876544210L), ("First Hero", 9876543230L)],
            rig.Updated.Select(u => (u.Profile.Name, u.Profile.Gold)));
    }

    [Fact]
    public void A_new_login_of_the_same_character_replaces_its_older_live_connection()
    {
        var rig = new Rig();
        var older = rig.Login(50001, "Same Hero");
        var newer = rig.Login(50002, "same hero", isn: 77000);

        older.Send(GoldExp(10, 1));
        newer.Send(GoldExp(20, 2));

        Assert.Equal(1, rig.Engine.Live.LiveConnections);
        Assert.Equal(9876543210L + 20, Assert.Single(rig.Updated).Profile.Gold);
    }

    [Fact]
    public void When_sixteen_connections_are_live_the_oldest_idle_one_makes_room_for_a_new_login()
    {
        var rig = new Rig();
        var lines = Enumerable.Range(0, CaptureEngine.MaxConnections)
            .Select(i => rig.Login(51000 + i, $"Hero {i}", isn: (uint)(1000 + i * 100_000)))
            .ToList();
        lines[0].Send(GoldExp(1, 1));
        lines[1].Send(GoldExp(1, 1));

        var latest = rig.Login(52000, "Late Hero", isn: 5_000_000);
        latest.Send(GoldExp(5, 5));

        Assert.Equal(CaptureEngine.MaxConnections, rig.Engine.Live.LiveConnections);
        Assert.Equal("Late Hero", rig.Updated[^1].Profile.Name);
        var updatesBefore = rig.Updated.Count;
        lines[2].Send(GoldExp(1, 1));
        Assert.Equal(updatesBefore, rig.Updated.Count);
        lines[0].Send(GoldExp(1, 1));
        Assert.Equal(updatesBefore + 1, rig.Updated.Count);
    }

    [Fact]
    public void Drop_item_and_get_drop_on_one_connection_add_the_item_and_never_cross_to_another()
    {
        var rig = new Rig();
        var first = rig.Login(50001, "First Hero");
        var second = rig.Login(50002, "Second Hero", isn: 77000);
        var definition = new Dictionary<string, object?> { ["ItemID"] = "77", ["sName"] = "Fictional Axe", ["sType"] = "Axe", ["iLvl"] = 30, ["iQty"] = 1, ["iStk"] = 1, ["bCoins"] = 1 };
        var drop = PlainJson(Envelope("dropItem", Body(("items", new Dictionary<string, object?> { ["77"] = definition }))));
        var pick = PlainJson(Envelope("getDrop", Body(("bSuccess", 1), ("ItemID", 77), ("CharItemID", 8800), ("iQty", 1), ("iQtyNow", 1), ("bBank", 0))));

        first.Send(drop);
        second.Send(pick);
        Assert.Empty(rig.Updated);
        Assert.Equal(1, rig.Engine.Live.LiveEventsUnmatched);

        first.Send(pick);
        var updated = Assert.Single(rig.Updated);
        Assert.Equal("First Hero", updated.Profile.Name);
        Assert.Equal("Fictional Axe", updated.Items[^1].Name);
        Assert.Equal(8800, updated.Items[^1].CharItemId);
    }

    [Fact]
    public void Events_that_change_nothing_are_counted_but_raise_nothing()
    {
        var rig = new Rig();
        var line = rig.Login();

        line.Send(
            PlainJson(Envelope("addItems", Body(("items", new Dictionary<string, object?> { ["1"] = new Dictionary<string, object?> { ["CharItemID"] = 1, ["iQtyNow"] = 5 } })))),
            PlainJson(Envelope("updateClass", Body(("sClassName", "X")))));

        Assert.Empty(rig.Updated);
        var live = rig.Engine.Live;
        Assert.Equal((0, 1, 1), (live.LiveEventsApplied, live.LiveEventsIgnored, live.LiveEventsUnmatched));
    }

    [Fact]
    public void A_failing_update_subscriber_does_not_stop_the_capture()
    {
        var rig = new Rig();
        rig.Engine.CharacterUpdated += _ => throw new InvalidOperationException("boom");
        var line = rig.Login();

        var thrown = Record.Exception(() => line.Send(GoldExp(1, 1), GoldExp(2, 2)));

        Assert.Null(thrown);
        Assert.Equal(2, rig.Updated.Count);
    }

    [Fact]
    public void Markers_never_leak_into_the_diagnostics_or_the_snapshots()
    {
        var rig = new Rig();
        var line = rig.Login();
        var noisy = Envelope("addGoldExp", Body(("intGold", 5), ("intExp", 5), ("strEmail", SecretEmail), ("strIP", SecretIp), ("uName", SecretPlayer)));

        line.Send(
            PlainJson(noisy),
            EncodeBlob(noisy),
            Bytes("{\"t\":\"xt\",\"b\":{\"o\":{\"cmd\":\"" + SecretEmail + "\",\"x\":}}}"),
            Bytes("%xt%" + SecretPassword + "%"),
            PlainJson(Envelope("ct", Body(("p", new Dictionary<string, object?> { [SecretPlayer] = 1 })))));

        foreach (var snapshot in rig.Captured.Concat(rig.Updated))
        {
            AssertNoSecrets(snapshot);
        }

        AssertNoSecrets(rig.Engine.Live);
        AssertNoSecrets(rig.Engine.Decoding);
        AssertNoSecrets(rig.Engine.Connections);
        AssertNoSecrets(rig.Engine.Counters);
        AssertNoSecrets(rig.Engine.Stages);
        Assert.DoesNotContain(SecretToken, JsonSerializer.Serialize(rig.Engine.Decoding), StringComparison.Ordinal);
    }
}
