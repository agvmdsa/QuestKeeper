using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Capture;

public class PcapStreamSourceTests
{
    private static async Task<List<TcpSegment>> ReadAll(byte[] pcap, CancellationToken token = default)
    {
        var segments = new List<TcpSegment>();
        await new PcapStreamSource(new MemoryStream(pcap)).RunAsync(segments.Add, token);
        return segments;
    }

    [Fact]
    public async Task Only_game_port_segments_are_delivered_in_file_order()
    {
        var pcap = PcapBuilder.Pcap(
        [
            PcapBuilder.TcpFrame(50000, 5588, 1, payload: [1]),
            PcapBuilder.TcpFrame(50000, 443, 2, payload: [2]),
            PcapBuilder.TcpFrame(5590, 50000, 3, payload: [3]),
            PcapBuilder.TcpFrame(50001, 5595, 4, payload: [4]),
            PcapBuilder.TcpFrame(50000, 5594, 5, payload: [5]),
        ]);

        var segments = await ReadAll(pcap);

        Assert.Equal([1u, 3u, 5u], segments.Select(s => s.Sequence));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Byte_order_and_timestamp_resolution_do_not_matter(bool bigEndian, bool nanosecond)
    {
        var pcap = PcapBuilder.Pcap([PcapBuilder.TcpFrame(payload: [9, 9])], bigEndian: bigEndian, nanosecond: nanosecond);

        var segments = await ReadAll(pcap);

        Assert.Equal([9, 9], Assert.Single(segments).Payload);
    }

    [Theory]
    [InlineData(PcapBuilder.BsdLoopback)]
    [InlineData(PcapBuilder.RawIp)]
    public async Task Link_type_from_the_file_header_is_used(int linkType)
    {
        var pcap = PcapBuilder.Pcap([PcapBuilder.TcpFrame(payload: [7], linkType: linkType)], linkType);

        var segments = await ReadAll(pcap);

        Assert.Single(segments);
    }

    [Fact]
    public async Task File_cut_in_the_middle_of_a_record_keeps_earlier_segments()
    {
        var full = PcapBuilder.Pcap(
        [
            PcapBuilder.TcpFrame(sequence: 1, payload: [1]),
            PcapBuilder.TcpFrame(sequence: 2, payload: [2, 2, 2, 2]),
        ]);
        var cut = full.AsSpan(0, full.Length - 3).ToArray();

        var segments = await ReadAll(cut);

        Assert.Equal(1u, Assert.Single(segments).Sequence);
    }

    [Fact]
    public async Task File_cut_in_a_record_header_ends_quietly()
    {
        var full = PcapBuilder.Pcap([PcapBuilder.TcpFrame(sequence: 1), PcapBuilder.TcpFrame(sequence: 2)]);
        var firstRecordEnd = 24 + 16 + PcapBuilder.TcpFrame().Length;
        var cut = full.AsSpan(0, firstRecordEnd + 7).ToArray();

        var segments = await ReadAll(cut);

        Assert.Single(segments);
    }

    [Fact]
    public async Task Invalid_magic_delivers_nothing()
    {
        var pcap = PcapBuilder.Pcap([PcapBuilder.TcpFrame()]);
        pcap[0] = 0x00;

        Assert.Empty(await ReadAll(pcap));
    }

    [Fact]
    public async Task Short_global_header_delivers_nothing()
    {
        Assert.Empty(await ReadAll(new byte[10]));
    }

    [Fact]
    public async Task Empty_stream_delivers_nothing()
    {
        Assert.Empty(await ReadAll([]));
    }

    [Fact]
    public async Task Absurd_record_length_ends_without_throwing()
    {
        using var ms = new MemoryStream();
        PcapBuilder.WriteGlobalHeader(ms, PcapBuilder.Ethernet, bigEndian: false, nanosecond: false);
        var record = new byte[16];
        BitConverter.GetBytes(uint.MaxValue).CopyTo(record, 8);
        ms.Write(record);

        Assert.Empty(await ReadAll(ms.ToArray()));
    }

    [Fact]
    public async Task Malformed_frames_are_skipped_and_reading_continues()
    {
        var pcap = PcapBuilder.Pcap(
        [
            [1, 2, 3],
            PcapBuilder.TcpFrame(sequence: 8),
        ]);

        var segments = await ReadAll(pcap);

        Assert.Equal(8u, Assert.Single(segments).Sequence);
    }

    [Fact]
    public async Task Cancellation_stops_reading()
    {
        var pcap = PcapBuilder.Pcap(Enumerable.Range(0, 50).Select(i => PcapBuilder.TcpFrame(sequence: (uint)i)));
        using var cts = new CancellationTokenSource();
        var count = 0;

        await new PcapStreamSource(new MemoryStream(pcap)).RunAsync(_ =>
        {
            count++;
            cts.Cancel();
        }, cts.Token);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Already_cancelled_token_delivers_nothing()
    {
        var pcap = PcapBuilder.Pcap([PcapBuilder.TcpFrame()]);

        Assert.Empty(await ReadAll(pcap, new CancellationToken(true)));
    }

    [Fact]
    public async Task Large_file_is_read_in_one_streaming_pass()
    {
        const int total = 10_000;
        var pcap = PcapBuilder.Pcap(Enumerable.Range(0, total).Select(i => PcapBuilder.TcpFrame(sequence: (uint)i, payload: [1, 2, 3])));
        var stream = new ReadTrackingStream(pcap);
        var count = 0;
        uint last = 0;
        var ordered = true;

        await new PcapStreamSource(stream).RunAsync(s =>
        {
            ordered &= count == 0 || s.Sequence == last + 1;
            last = s.Sequence;
            count++;
        }, CancellationToken.None);

        Assert.Equal(total, count);
        Assert.True(ordered);
        Assert.True(stream.LargestRead <= 1 << 18, "largest read " + stream.LargestRead);
        Assert.True(stream.ReadCalls >= total, "reads " + stream.ReadCalls);
    }

    private sealed class ReadTrackingStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public int ReadCalls { get; private set; }
        public int LargestRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Track(count);
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Track(buffer.Length);
            return _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Track(buffer.Length);
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Track(count);
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        private void Track(int requested)
        {
            ReadCalls++;
            LargestRead = Math.Max(LargestRead, requested);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
