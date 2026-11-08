using System.Text;
using AqwCompanion.Core.Capture;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.Capture;

public class StreamReassemblerTests
{
    private static List<string> Text(IEnumerable<byte[]> messages) => [.. messages.Select(Encoding.UTF8.GetString)];

    private static List<string> FeedAll(StreamReassembler reassembler, params TcpSegment[] segments) =>
        Text(segments.SelectMany(reassembler.Feed));

    [Fact]
    public void Connection_key_uses_upper_case_hex_addresses_and_value_equality()
    {
        var key = ConnectionKey.From(ServerData(1, [1]));

        Assert.Equal(new ConnectionKey("22780809", 5588, "C0A80114", 50000), key);
        Assert.Equal(key, ConnectionKey.From(ServerData(99, [2])));
        Assert.NotEqual(key, ConnectionKey.From(ServerData(1, [1], clientPort: 50001)));
    }

    [Fact]
    public void A_message_split_across_three_segments_is_joined()
    {
        var reassembler = new StreamReassembler();

        var messages = FeedAll(reassembler,
            SynAck(99),
            ServerData(100, Bytes("hel")),
            ServerData(103, Bytes("lo wor")),
            ServerData(109, Terminated("ld")));

        Assert.Equal(["hello world"], messages);
    }

    [Fact]
    public void Two_messages_in_one_segment_are_both_returned()
    {
        var messages = FeedAll(new StreamReassembler(), SynAck(0), ServerData(1, [.. Terminated("one"), .. Terminated("two")]));

        Assert.Equal(["one", "two"], messages);
    }

    [Fact]
    public void Swapped_neighbours_are_put_in_order()
    {
        var messages = FeedAll(new StreamReassembler(),
            SynAck(0),
            ServerData(1, Terminated("aa")),
            ServerData(7, Terminated("cc")),
            ServerData(4, Terminated("bb")));

        Assert.Equal(["aa", "bb", "cc"], messages);
    }

    [Fact]
    public void The_first_two_data_segments_swapped_after_the_syn_are_put_in_order()
    {
        var messages = FeedAll(new StreamReassembler(),
            SynAck(500),
            ServerData(505, Terminated("second")),
            ServerData(501, Terminated("one")));

        Assert.Equal(["one", "second"], messages);
    }

    [Fact]
    public void Without_a_syn_the_first_data_segment_sets_the_start()
    {
        var messages = FeedAll(new StreamReassembler(), ServerData(7000, Bytes("ab")), ServerData(7002, Terminated("c")));

        Assert.Equal(["abc"], messages);
    }

    [Fact]
    public void A_retransmitted_segment_is_ignored()
    {
        var messages = FeedAll(new StreamReassembler(),
            SynAck(0),
            ServerData(1, Terminated("one")),
            ServerData(1, Terminated("one")),
            ServerData(5, Terminated("two")));

        Assert.Equal(["one", "two"], messages);
    }

    [Fact]
    public void An_overlapping_retransmission_with_extra_bytes_adds_only_the_new_bytes()
    {
        var messages = FeedAll(new StreamReassembler(),
            SynAck(0),
            ServerData(1, Bytes("abc")),
            ServerData(2, Terminated("bcdef")));

        Assert.Equal(["abcdef"], messages);
    }

    [Fact]
    public void Sequence_numbers_wrap_around_the_32_bit_range()
    {
        var messages = FeedAll(new StreamReassembler(),
            SynAck(4294966899),
            ServerData(4294966900, Terminated("before")),
            ServerData(4294966907, Terminated("wrap")),
            ServerData(unchecked(4294966907 + 5), Terminated("after")));

        Assert.Equal(["before", "wrap", "after"], messages);
    }

    [Fact]
    public void Out_of_order_across_the_wrap_is_put_in_order()
    {
        var messages = FeedAll(new StreamReassembler(),
            SynAck(4294967290),
            ServerData(unchecked(4294967291 + 5), Terminated("bb")),
            ServerData(4294967291, Terminated("aaaa")));

        Assert.Equal(["aaaa", "bb"], messages);
    }

    [Fact]
    public void More_than_the_allowed_held_segments_resyncs_without_throwing()
    {
        var reassembler = new StreamReassembler();
        reassembler.Feed(SynAck(0));
        reassembler.Feed(ServerData(1, Bytes("partial-")));

        // A gap: the segment at 1000 never arrives; later ones are held.
        var messages = new List<byte[]>();
        for (uint i = 0; i <= StreamReassembler.MaxPendingSegments; i++)
        {
            messages.AddRange(reassembler.Feed(ServerData(2000 + (i * 10), Terminated($"m{i:D3}"))));
        }

        // The resync starts at the closest held segment (2000) and drops the partial buffer.
        Assert.Equal(["m000"], Text(messages));
        Assert.Equal("next", Text(reassembler.Feed(ServerData(2005, Terminated("next"))))[0]);
    }

    [Fact]
    public void An_oversized_message_without_a_nul_is_dropped_and_the_next_message_still_parses()
    {
        var reassembler = new StreamReassembler();
        reassembler.Feed(SynAck(0));
        uint sequence = 1;
        var chunk = new byte[256 * 1024];
        Array.Fill(chunk, (byte)'x');
        var messages = new List<byte[]>();
        for (var i = 0; i < 12; i++)
        {
            messages.AddRange(reassembler.Feed(ServerData(sequence, chunk)));
            sequence += (uint)chunk.Length;
        }
        messages.AddRange(reassembler.Feed(ServerData(sequence, [.. Bytes("tail"), 0, .. Terminated("good")])));

        Assert.Equal(["good"], Text(messages));
    }

    [Fact]
    public void A_complete_message_over_the_limit_is_dropped()
    {
        var big = new byte[StreamReassembler.MaxMessageBytes + 1];
        Array.Fill(big, (byte)'x');

        var messages = FeedAll(new StreamReassembler(), SynAck(0), ServerData(1, [.. big, 0, .. Terminated("ok")]));

        Assert.Equal(["ok"], messages);
    }

    [Fact]
    public void A_message_exactly_at_the_limit_is_kept()
    {
        var big = new byte[StreamReassembler.MaxMessageBytes];
        Array.Fill(big, (byte)'x');

        var messages = new StreamReassembler().Feed(ServerData(1, [.. big, 0]));

        Assert.Equal(StreamReassembler.MaxMessageBytes, Assert.Single(messages).Length);
    }

    [Fact]
    public void Empty_messages_between_two_nuls_are_skipped()
    {
        var messages = FeedAll(new StreamReassembler(), SynAck(0), ServerData(1, [0, 0, .. Terminated("a"), 0]));

        Assert.Equal(["a"], messages);
    }

    [Fact]
    public void A_syn_clears_the_partial_buffer_and_held_segments()
    {
        var reassembler = new StreamReassembler();
        reassembler.Feed(SynAck(0));
        reassembler.Feed(ServerData(1, Bytes("half")));
        reassembler.Feed(ServerData(50, Terminated("held")));

        reassembler.Feed(SynAck(1000));
        var messages = FeedAll(reassembler, ServerData(1001, Terminated("fresh")));

        Assert.Equal(["fresh"], messages);
    }

    [Fact]
    public void Segments_without_payload_return_nothing()
    {
        Assert.Empty(new StreamReassembler().Feed(ServerData(1, [])));
    }
}
