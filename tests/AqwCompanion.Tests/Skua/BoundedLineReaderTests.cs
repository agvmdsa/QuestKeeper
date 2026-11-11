using System.Text;
using AqwCompanion.App.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class BoundedLineReaderTests
{
    private static async Task<List<LineResult>> ReadAll(Stream stream, CancellationToken ct = default)
    {
        var results = new List<LineResult>();
        await foreach (var r in BoundedLineReader.ReadAsync(stream, ct))
        {
            results.Add(r);
        }

        return results;
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Two_lines_give_two_results()
    {
        var results = await ReadAll(Bytes("one\ntwo\n"));

        Assert.Equal([new LineResult("one", false), new LineResult("two", false)], results);
    }

    [Fact]
    public async Task A_carriage_return_before_the_newline_is_trimmed()
    {
        Assert.Equal([new LineResult("one", false)], await ReadAll(Bytes("one\r\n")));
    }

    [Fact]
    public async Task Empty_lines_are_skipped()
    {
        Assert.Equal([new LineResult("a", false), new LineResult("b", false)], await ReadAll(Bytes("\n\r\na\n\nb\n")));
    }

    [Fact]
    public async Task An_oversize_line_is_reported_once_and_the_next_line_still_arrives()
    {
        var results = await ReadAll(Bytes(new string('x', SkuaProtocol.MaxLineBytes + 1) + "\nnext\n"));

        Assert.Equal([new LineResult(null, true), new LineResult("next", false)], results);
    }

    [Fact]
    public async Task A_line_of_exactly_the_limit_is_accepted()
    {
        var text = new string('x', SkuaProtocol.MaxLineBytes);

        Assert.Equal([new LineResult(text, false)], await ReadAll(Bytes(text + "\n")));
    }

    [Fact]
    public async Task A_trailing_fragment_without_a_newline_is_ignored()
    {
        Assert.Equal([new LineResult("done", false)], await ReadAll(Bytes("done\npartial")));
    }

    [Fact]
    public async Task Delivery_one_byte_at_a_time_gives_the_same_result()
    {
        var text = "one\r\n" + new string('y', SkuaProtocol.MaxLineBytes + 5) + "\ntwo\n";

        var whole = await ReadAll(Bytes(text));
        var slow = await ReadAll(new OneByteStream(Encoding.UTF8.GetBytes(text)));

        Assert.Equal(whole, slow);
        Assert.Equal(3, slow.Count);
    }

    [Fact]
    public async Task Cancellation_stops_the_enumeration()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAll(Bytes("a\n"), cts.Token));
    }

    [Fact]
    public async Task Bytes_that_are_not_utf8_do_not_throw()
    {
        var stream = new MemoryStream([(byte)'a', 0xFF, 0xFE, (byte)'b', (byte)'\n']);

        var results = await ReadAll(stream);

        var single = Assert.Single(results);
        Assert.False(single.TooLong);
        Assert.StartsWith("a", single.Text);
        Assert.EndsWith("b", single.Text);
    }

    private sealed class OneByteStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= data.Length || buffer.Length == 0)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[0] = data[_position++];
            return ValueTask.FromResult(1);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
