using AqwCompanion.Core.Capture;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;
using static AqwCompanion.Tests.Capture.SecretScan;

namespace AqwCompanion.Tests.Capture;

public class CaptureEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (CaptureEngine Engine, List<CharacterSnapshot> Captured) NewEngine()
    {
        var engine = new CaptureEngine(new FixedClock());
        var captured = new List<CharacterSnapshot>();
        engine.CharacterCaptured += captured.Add;
        return (engine, captured);
    }

    private static void Feed(CaptureEngine engine, IEnumerable<TcpSegment> segments)
    {
        foreach (var segment in segments)
        {
            engine.Accept(segment);
        }
    }

    private static void AssertStandardSnapshot(CharacterSnapshot snapshot, string name = "Fictional Hero")
    {
        Assert.Equal(name, snapshot.Profile.Name);
        Assert.Equal(60, snapshot.Profile.Level);
        Assert.Equal(9876543210L, snapshot.Profile.Gold);
        Assert.Equal(Now, snapshot.CapturedAtUtc);
        Assert.Equal(
            [(5, 3, 5000, 1400, 6400), (6, 10, 302500, 0, 0), (7, 1, 0, 0, 900)],
            snapshot.Factions.Select(f => (f.FactionId, f.Rank, f.Rep, f.SpillRep, f.RepToRank)));
        Assert.Equal(["Fictional Blade", "Fictional Class"], snapshot.Items.Select(i => i.Name));
        Assert.Equal(9, snapshot.Items[1].ClassId);
        Assert.Equal(ItemFlags.House, Assert.Single(snapshot.HouseItems).Flags);
    }

    [Fact]
    public void One_full_login_gives_one_snapshot_with_the_expected_values()
    {
        var (engine, captured) = NewEngine();

        Feed(engine, Connection().All);

        AssertStandardSnapshot(Assert.Single(captured));
        Assert.Equal(1, engine.Counters.CharactersCaptured);
    }

    [Fact]
    public void The_inventory_arriving_before_the_profile_gives_the_same_snapshot()
    {
        var (engine, captured) = NewEngine();

        Feed(engine, Connection(inventoryFirst: true).All);

        AssertStandardSnapshot(Assert.Single(captured));
    }

    [Fact]
    public void Split_and_shuffled_segments_still_give_the_snapshot()
    {
        var (engine, captured) = NewEngine();
        var traffic = Connection(segmentSize: 300);
        var data = traffic.Server.Skip(1).OrderBy(_ => 0).ToList();
        var random = new Random(7);
        var shuffled = data.OrderBy(_ => random.Next()).ToList();

        engine.Accept(traffic.Server[0]);
        Feed(engine, shuffled);

        Assert.True(data.Count < StreamReassembler.MaxPendingSegments);
        AssertStandardSnapshot(Assert.Single(captured));
    }

    [Fact]
    public void Two_interleaved_connections_give_two_snapshots_with_different_names()
    {
        var (engine, captured) = NewEngine();
        var first = Connection(clientPort: 50001, name: "First Hero", segmentSize: 500).Server;
        var second = Connection(clientPort: 50002, name: "Second Hero", isn: 77000, segmentSize: 500).Server;

        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count)
            {
                engine.Accept(first[i]);
            }
            if (i < second.Count)
            {
                engine.Accept(second[i]);
            }
        }

        Assert.Equal(["First Hero", "Second Hero"], captured.Select(c => c.Profile.Name).Order());
    }

    [Fact]
    public void A_connection_that_never_sends_the_inventory_gives_no_snapshot()
    {
        var (engine, captured) = NewEngine();

        Feed(engine, Connection(includeInventory: false).All);

        Assert.Empty(captured);
        Assert.Equal(0, engine.Counters.CharactersCaptured);
    }

    [Fact]
    public void A_connection_that_never_sends_the_profile_gives_no_snapshot()
    {
        var (engine, captured) = NewEngine();

        Feed(engine, Connection(includeProfile: false).All);

        Assert.Empty(captured);
    }

    [Fact]
    public void After_completion_further_segments_are_not_decoded()
    {
        var (engine, captured) = NewEngine();
        var traffic = Connection();
        Feed(engine, traffic.Server);
        var before = engine.Counters;

        var extra = EncodeBlob(StandardInventory());
        engine.Accept(ServerData(traffic.NextServerSequence, [.. extra, 0]));
        engine.Accept(ServerData(traffic.NextServerSequence + (uint)extra.Length + 1, [.. extra, 0]));

        var after = engine.Counters;
        Assert.Single(captured);
        Assert.Equal(before.MessagesDecoded, after.MessagesDecoded);
        Assert.Equal(before.SegmentsDropped + 2, after.SegmentsDropped);
        Assert.Equal(before.SegmentsSeen + 2, after.SegmentsSeen);
    }

    [Fact]
    public void Messages_after_completion_in_the_same_segment_are_not_decoded()
    {
        var (engine, captured) = NewEngine();
        var traffic = Connection(segmentSize: 1_000_000, extraServerMessages: [EncodeBlob(StandardInventory()), EncodeBlob(InitUserData(ProfileData("Second Name")))]);

        Feed(engine, traffic.Server);

        Assert.Equal("Fictional Hero", Assert.Single(captured).Profile.Name);
        Assert.Equal(2, engine.Counters.MessagesDecoded);
    }

    [Fact]
    public void Client_to_server_segments_are_dropped_and_counted_never_decoded()
    {
        var (engine, captured) = NewEngine();

        // Even a valid game message from the client must never reach a decoder.
        engine.Accept(ClientData(1, PlainJson(Envelope("loadInventoryBig", new Dictionary<string, object?> { ["items"] = new List<object?>() }))));
        engine.Accept(ClientData(2, EncodeBlob(InitUserData())));
        Feed(engine, Connection().Client);

        Assert.Empty(captured);
        Assert.Equal(new CaptureCounters(4, 4, 0, 0), engine.Counters);
    }

    [Fact]
    public void In_a_full_login_the_client_segments_are_dropped_and_only_server_messages_are_decoded()
    {
        var (engine, captured) = NewEngine();
        var traffic = Connection();

        Feed(engine, traffic.Client);
        Feed(engine, traffic.Server);

        Assert.Single(captured);
        // Dropped: the client segments plus the server segments that arrive after the character is complete.
        Assert.True(engine.Counters.SegmentsDropped >= traffic.Client.Count);
        Assert.Equal(traffic.Client.Count + traffic.Server.Count, engine.Counters.SegmentsSeen);
        // The text and XML messages are not decoded, the other players' blobs come after completion: only the two parts count.
        Assert.Equal(2, engine.Counters.MessagesDecoded);
    }

    [Fact]
    public void The_stage_counters_count_the_parts_of_a_login_and_nothing_else()
    {
        var (engine, _) = NewEngine();
        Assert.Equal(default, engine.Stages);

        Feed(engine, Connection().All);

        Assert.Equal(new CaptureStageCounters(0, 1, 1, 0), engine.Stages);
    }

    [Fact]
    public void A_long_message_that_cannot_be_read_is_unreadable_but_text_and_xml_are_not()
    {
        var (engine, _) = NewEngine();
        var garbage = Bytes("Z" + new string('A', 60));
        var text = Bytes("%xt%" + new string('x', 60));
        var xml = Bytes("<msg>" + new string('x', 60) + "</msg>");
        var shortGarbage = Bytes("ZAAAA");
        var stream = new[] { garbage, text, xml, shortGarbage }.SelectMany(m => m.Append((byte)0)).ToArray();

        engine.Accept(SynAck(1000));
        engine.Accept(ServerData(1001, stream));

        Assert.Equal(new CaptureStageCounters(1, 0, 0, 0), engine.Stages);
        Assert.Equal(0, engine.Counters.MessagesDecoded);
    }

    [Fact]
    public void A_readable_message_with_another_command_counts_as_other()
    {
        var (engine, _) = NewEngine();
        var message = PlainJson(Envelope("mtls", new Dictionary<string, object?> { ["id"] = 1 }));

        engine.Accept(SynAck(1000));
        engine.Accept(ServerData(1001, [.. message, 0]));

        Assert.Equal(new CaptureStageCounters(0, 0, 0, 1), engine.Stages);
        Assert.Equal(1, engine.Counters.MessagesDecoded);
    }

    [Fact]
    public void Segments_between_other_ports_are_dropped()
    {
        var (engine, _) = NewEngine();

        engine.Accept(new TcpSegment(PcapBuilder.ServerAddress, 443, PcapBuilder.ClientAddress, 50000, 1, false, [1, 2, 3]));

        Assert.Equal(new CaptureCounters(1, 1, 0, 0), engine.Counters);
    }

    [Fact]
    public void A_segment_between_two_game_ports_is_ambiguous_and_dropped()
    {
        var (engine, _) = NewEngine();

        // A client whose own port falls in the game range: its segments to the server must still never enter the pipeline.
        engine.Accept(new TcpSegment(
            PcapBuilder.ClientAddress, 5590, PcapBuilder.ServerAddress, GameCaptureBuilder.GamePort, 1, false,
            GameCaptureBuilder.Terminated("{\"b\":{\"o\":{\"cmd\":\"initUserData\"}}}")));

        Assert.Equal(new CaptureCounters(1, 1, 0, 0), engine.Counters);
    }

    [Fact]
    public void The_seventeenth_connection_is_ignored()
    {
        var (engine, captured) = NewEngine();
        var partial = Enumerable.Range(0, CaptureEngine.MaxConnections)
            .Select(i => Connection(clientPort: 51000 + i, name: $"Hero {i}", includeInventory: false))
            .ToList();
        foreach (var traffic in partial)
        {
            Feed(engine, traffic.Server);
        }

        Feed(engine, Connection(clientPort: 52000, name: "Late Hero").Server);

        Assert.Empty(captured);

        // The tracked ones can still finish.
        var rest = EncodeBlob(StandardInventory());
        engine.Accept(ServerData(partial[0].NextServerSequence, [.. rest, 0], 51000));
        Assert.Equal("Hero 0", Assert.Single(captured).Profile.Name);
    }

    [Fact]
    public void Garbage_and_a_decompression_bomb_do_not_stop_later_valid_messages()
    {
        var (engine, captured) = NewEngine();
        var bomb = new byte[MessageDecoder.MaxInflatedBytes + 1024 * 1024];
        Array.Fill(bomb, (byte)' ');
        bomb[0] = (byte)'{';
        var garbage = new byte[3000];
        new Random(3).NextBytes(garbage);
        for (var i = 0; i < garbage.Length; i++)
        {
            garbage[i] = garbage[i] == 0 ? (byte)1 : garbage[i];
        }

        var traffic = Connection(leadingServerMessages: [garbage, EncodeBlobText(bomb), Bytes("{broken"), Bytes("%xt%junk")]);

        var thrown = Record.Exception(() => Feed(engine, traffic.All));

        Assert.Null(thrown);
        AssertStandardSnapshot(Assert.Single(captured));
        Assert.Equal(2, engine.Counters.MessagesDecoded);
    }

    [Fact]
    public void An_exception_in_a_subscriber_does_not_escape()
    {
        var engine = new CaptureEngine(new FixedClock());
        engine.CharacterCaptured += _ => throw new InvalidOperationException("boom");

        var thrown = Record.Exception(() => Feed(engine, Connection().All));

        Assert.Null(thrown);
        Assert.Equal(1, engine.Counters.CharactersCaptured);
    }

    [Fact]
    public void A_null_payload_does_not_escape_as_an_exception()
    {
        var engine = new CaptureEngine(new FixedClock());

        var thrown = Record.Exception(() => engine.Accept(new TcpSegment(PcapBuilder.ServerAddress, 5588, PcapBuilder.ClientAddress, 1, 1, false, null!)));

        Assert.Null(thrown);
    }

    [Fact]
    public async Task End_to_end_through_the_real_pcap_seam_gives_the_same_snapshot()
    {
        var traffic = Connection(segmentSize: 400);
        var frames = traffic.Client.Take(1)
            .Concat(traffic.Server)
            .Concat(traffic.Client.Skip(1))
            .Select(ToFrame)
            .ToList();
        var pcap = PcapBuilder.Pcap(frames);
        var (engine, captured) = NewEngine();

        await new PcapStreamSource(new MemoryStream(pcap)).RunAsync(engine.Accept, CancellationToken.None);

        AssertStandardSnapshot(Assert.Single(captured));
        Assert.True(engine.Counters.SegmentsDropped >= traffic.Client.Count);
    }

    private static byte[] ToFrame(TcpSegment segment) =>
        PcapBuilder.TcpFrame(
            segment.SourcePort,
            segment.DestinationPort,
            segment.Sequence,
            segment.IsSyn,
            segment.Payload,
            srcAddress: segment.SourceAddress,
            dstAddress: segment.DestinationAddress);

    [Fact]
    public void Privacy_no_marker_secret_reaches_the_snapshot()
    {
        var (engine, captured) = NewEngine();

        Feed(engine, Connection().All);

        var snapshot = Assert.Single(captured);
        AssertNoSecrets(snapshot);
        AssertNoSecrets(snapshot.Profile);
        AssertNoSecrets(snapshot.Factions);
        AssertNoSecrets(snapshot.Items);
        AssertNoSecrets(snapshot.HouseItems);
        AssertNoSecrets(engine.Counters);
    }

    [Fact]
    public async Task Privacy_holds_through_the_pcap_seam_as_well()
    {
        var traffic = Connection(segmentSize: 333);
        var pcap = PcapBuilder.Pcap(traffic.All.Select(ToFrame));
        var (engine, captured) = NewEngine();

        await new PcapStreamSource(new MemoryStream(pcap)).RunAsync(engine.Accept, CancellationToken.None);

        AssertNoSecrets(Assert.Single(captured));
    }
}
