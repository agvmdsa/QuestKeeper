using System.Text;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaCommandReaderTests
{
    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<List<SkuaCommand>> ReadAll(Stream stream)
    {
        var commands = new List<SkuaCommand>();
        await SkuaCommandReader.ReadAsync(stream, commands.Add, default);
        return commands;
    }

    private static string Line(SkuaCommand command) => SkuaLine.ToCommandLine(command)!;

    [Fact]
    public async Task Each_command_line_is_handed_over_in_order()
    {
        var text = Line(new ListScriptsCommand("a")) + "\n" + Line(new StartCommand("b", "0123456789abcdef")) + "\r\n" + Line(new StopCommand("c")) + "\n";

        var commands = await ReadAll(Bytes(text));

        Assert.Equal([new ListScriptsCommand("a"), new StartCommand("b", "0123456789abcdef"), new StopCommand("c")], commands);
    }

    [Fact]
    public async Task A_line_over_the_command_cap_is_dropped_and_the_next_one_still_read()
    {
        var oversize = Line(new StopCommand("x")).Replace("\"x\"", "\"" + new string('x', SkuaProtocol.MaxCommandLineBytes) + "\"", StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(oversize) > SkuaProtocol.MaxCommandLineBytes);

        var commands = await ReadAll(Bytes(oversize + "\n" + Line(new StopCommand("next")) + "\n"));

        Assert.Equal([new StopCommand("next")], commands);
    }

    [Fact]
    public async Task A_line_of_exactly_the_cap_is_read()
    {
        var line = Line(new StopCommand("x"));
        var padded = line.Replace("\"x\"", "\"" + new string('x', SkuaProtocol.MaxCommandLineBytes - Encoding.UTF8.GetByteCount(line) + 1) + "\"", StringComparison.Ordinal);
        Assert.Equal(SkuaProtocol.MaxCommandLineBytes, Encoding.UTF8.GetByteCount(padded));

        var commands = await ReadAll(Bytes(padded + "\n"));

        Assert.Single(commands);
    }

    [Fact]
    public async Task Unknown_malformed_and_non_command_lines_are_ignored()
    {
        var text = string.Join(
            "\n",
            """{"v":1,"type":"launchMissiles","reqId":"a"}""",
            "not json",
            """{"v":1,"type":"start","reqId":"b"}""",
            """{"v":2,"type":"stop","reqId":"c"}""",
            SkuaLine.ToLine(new HelloMessage(new SkuaHello("1", "2"))),
            "",
            """{"v":1,"type":"stop","reqId":"d","path":"C:\\evil.cs"}""") + "\n";

        var commands = await ReadAll(Bytes(text));

        Assert.Equal([new StopCommand("d")], commands);
    }

    [Fact]
    public async Task A_failing_handler_does_not_stop_the_reading()
    {
        var seen = new List<SkuaCommand>();
        var text = Line(new StopCommand("a")) + "\n" + Line(new StopCommand("b")) + "\n";

        await SkuaCommandReader.ReadAsync(Bytes(text), c =>
        {
            seen.Add(c);
            throw new InvalidOperationException("handler");
        }, default);

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task A_broken_stream_ends_the_reading_quietly()
    {
        await SkuaCommandReader.ReadAsync(new BrokenStream(), _ => { }, default);
    }

    [Fact]
    public async Task A_cancelled_reading_ends_quietly()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await SkuaCommandReader.ReadAsync(new BrokenStream(), _ => { }, cancel.Token);
    }

    [Fact]
    public async Task A_partial_last_line_without_a_newline_is_not_handed_over()
    {
        var commands = await ReadAll(Bytes(Line(new StopCommand("a")) + "\n" + Line(new StopCommand("b"))));

        Assert.Equal([new StopCommand("a")], commands);
    }

    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Pipe is broken.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<int>(cancellationToken) : throw new IOException("Pipe is broken.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
