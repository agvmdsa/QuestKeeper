using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Capture;

public class FrameParserTests
{
    private static readonly byte[] Payload = [1, 2, 3, 4, 5];

    [Fact]
    public void Ethernet_ipv4_tcp_frame_is_parsed()
    {
        var frame = PcapBuilder.TcpFrame(50000, 5588, 4242, syn: false, payload: Payload);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));

        Assert.Equal(PcapBuilder.ClientAddress, seg.SourceAddress);
        Assert.Equal(PcapBuilder.ServerAddress, seg.DestinationAddress);
        Assert.Equal(50000, seg.SourcePort);
        Assert.Equal(5588, seg.DestinationPort);
        Assert.Equal(4242u, seg.Sequence);
        Assert.False(seg.IsSyn);
        Assert.Equal(Payload, seg.Payload);
    }

    [Fact]
    public void Syn_flag_is_reported()
    {
        var frame = PcapBuilder.TcpFrame(syn: true);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));
        Assert.True(seg.IsSyn);
        Assert.Empty(seg.Payload);
    }

    [Fact]
    public void Ip_header_options_are_skipped()
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload, ipOptionBytes: 4);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));
        Assert.Equal(5588, seg.DestinationPort);
        Assert.Equal(Payload, seg.Payload);
    }

    [Fact]
    public void Tcp_header_options_are_skipped()
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload, tcpOptionBytes: 12);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));
        Assert.Equal(Payload, seg.Payload);
    }

    [Fact]
    public void Both_option_kinds_together_are_skipped()
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload, ipOptionBytes: 8, tcpOptionBytes: 4);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));
        Assert.Equal(Payload, seg.Payload);
    }

    [Fact]
    public void Vlan_tagged_frame_is_parsed()
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload, vlan: true);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));
        Assert.Equal(Payload, seg.Payload);
    }

    [Theory]
    [InlineData(PcapBuilder.BsdLoopback)]
    [InlineData(PcapBuilder.RawIpBsd)]
    [InlineData(PcapBuilder.RawIp)]
    public void Loopback_and_raw_ip_link_types_are_parsed(int linkType)
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload, linkType: linkType);

        Assert.True(FrameParser.TryParse(linkType, frame, out var seg));
        Assert.Equal(5588, seg.DestinationPort);
        Assert.Equal(Payload, seg.Payload);
    }

    [Fact]
    public void Ethernet_padding_after_the_ip_packet_is_not_payload()
    {
        var frame = PcapBuilder.TcpFrame(payload: [], ethernetPadding: 6);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));
        Assert.Empty(seg.Payload);
    }

    [Fact]
    public void Payload_is_a_copy_owned_by_the_caller()
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload);

        Assert.True(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out var seg));
        Array.Clear(frame);

        Assert.Equal(Payload, seg.Payload);
    }

    [Theory]
    [InlineData(17)] // UDP
    [InlineData(1)]  // ICMP
    public void Non_tcp_ip_protocols_are_rejected(byte protocol)
    {
        var frame = PcapBuilder.TcpFrame(protocol: protocol);

        Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out _));
    }

    [Fact]
    public void Arp_frame_is_rejected()
    {
        var frame = PcapBuilder.TcpFrame();
        frame[12] = 0x08;
        frame[13] = 0x06;

        Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out _));
    }

    [Fact]
    public void Ipv6_frame_is_rejected()
    {
        var frame = PcapBuilder.TcpFrame();
        frame[12] = 0x86;
        frame[13] = 0xDD;
        frame[14] = 0x60;

        Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out _));
    }

    [Fact]
    public void Ip_header_with_ihl_below_five_is_rejected()
    {
        var frame = PcapBuilder.TcpFrame();
        frame[14] = 0x44;

        Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out _));
    }

    [Fact]
    public void Fragmented_ip_packet_is_rejected()
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload, fragmentOffset: 0x00B9);

        Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out _));
    }

    [Fact]
    public void Truncated_frames_are_rejected_at_every_length_without_throwing()
    {
        var frame = PcapBuilder.TcpFrame(payload: Payload, ipOptionBytes: 4, tcpOptionBytes: 4);
        var headerEnd = 14 + 24 + 24;

        for (var length = 0; length < headerEnd; length++)
        {
            Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame.AsSpan(0, length), out _), "length " + length);
        }
    }

    [Fact]
    public void Truncated_tcp_header_is_rejected()
    {
        var frame = PcapBuilder.TcpFrame();

        Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame.AsSpan(0, 14 + 20 + 10), out _));
    }

    [Fact]
    public void Tcp_data_offset_below_five_is_rejected()
    {
        var frame = PcapBuilder.TcpFrame();
        frame[14 + 20 + 12] = 0x30;

        Assert.False(FrameParser.TryParse(PcapBuilder.Ethernet, frame, out _));
    }

    [Fact]
    public void Unknown_link_type_is_rejected()
    {
        var frame = PcapBuilder.TcpFrame();

        Assert.False(FrameParser.TryParse(999, frame, out _));
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(7);
        var buffer = new byte[80];
        foreach (var linkType in new[] { 0, 1, 12, 101, 7 })
        {
            for (var i = 0; i < 500; i++)
            {
                random.NextBytes(buffer);
                FrameParser.TryParse(linkType, buffer.AsSpan(0, random.Next(0, buffer.Length)), out _);
            }
        }
    }

    [Theory]
    [InlineData(5587, false)]
    [InlineData(5588, true)]
    [InlineData(5594, true)]
    [InlineData(5595, false)]
    public void Game_port_range_is_inclusive(int port, bool expected)
    {
        Assert.Equal(expected, GameTraffic.IsGamePort(port));
    }

    [Fact]
    public void Capture_filter_matches_the_port_range()
    {
        Assert.Equal("tcp and portrange 5588-5594", GameTraffic.CaptureFilter);
    }
}
