using System.Text;
using System.Threading.Channels;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Web;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneEventStreamTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 6, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    private class RecordingStream : Stream
    {
        private readonly StringBuilder _text = new();

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_text)
            {
                _text.Append(Encoding.UTF8.GetString(buffer, offset, count));
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_text)
            {
                _text.Append(Encoding.UTF8.GetString(buffer.Span));
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Accepts the first frame, then fails every write with an OperationCanceledException that is not the caller's token.</summary>
    private sealed class FailingAfterFirstFrame : RecordingStream
    {
        private int _writes;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _writes) == 1
                ? base.WriteAsync(buffer, cancellationToken)
                : throw new OperationCanceledException("the output stream gave up");
    }

    private static FeedSnapshot Base(string selected = "Alpha", int rank = 8) =>
        new(1, new FeedCapture("Listening.", true), selected,
            [new FeedCharacter("Alpha", 60, true, T0, T0, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null)],
            [new FeedFaction("Yokai", rank, 0, 0, 0, false)], null);

    private sealed class Rig : IDisposable
    {
        public ManualClock Clock { get; } = new(T0);
        public FakeFeedSource Source { get; } = new(Base());
        public PhoneHub Hub { get; }

        public Rig() => Hub = new PhoneHub(Source, new NoticeEngine(), new NoticeLog(50), Clock.GetUtcNow);

        public void Dispose() => Hub.Dispose();

        public void Change(FeedSnapshot snapshot)
        {
            Source.Current = snapshot;
            Source.Raise();
        }
    }

    private static int Count(string text, string frameStart)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(frameStart, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += frameStart.Length;
        }

        return count;
    }

    [Fact]
    public void Format_writes_an_event_and_a_data_line_and_a_blank_line()
    {
        Assert.Equal("event: state\ndata: {}\n\n", PhoneEventStream.Format("state", "{}"));
    }

    [Fact]
    public async Task The_first_frame_is_the_current_state()
    {
        using var rig = new Rig();
        var output = new RecordingStream();
        using var cts = new CancellationTokenSource();

        var run = PhoneEventStream.RunAsync(output, rig.Hub, Quiet, cts.Token);
        var first = PhoneEventStream.Format("state", rig.Hub.CurrentJson);
        await Wait.Until(() => output.Text.Length >= first.Length);
        cts.Cancel();
        await run;

        Assert.Equal(first, output.Text);
    }

    [Fact]
    public async Task A_change_after_the_start_adds_a_state_frame()
    {
        using var rig = new Rig();
        var output = new RecordingStream();
        using var cts = new CancellationTokenSource();
        var run = PhoneEventStream.RunAsync(output, rig.Hub, Quiet, cts.Token);
        await Wait.Until(() => rig.Hub.SubscriberCount == 1 && output.Text.Contains("event: state"));

        rig.Change(Base(selected: "Bravo"));
        await Wait.Until(() => Count(output.Text, "event: state\n") == 2);
        cts.Cancel();
        await run;

        Assert.EndsWith(PhoneEventStream.Format("state", rig.Hub.CurrentJson), output.Text);
        Assert.DoesNotContain("event: notice", output.Text);
    }

    [Fact]
    public async Task A_rank_up_adds_a_notice_frame_before_the_state_frame()
    {
        using var rig = new Rig();
        var output = new RecordingStream();
        using var cts = new CancellationTokenSource();
        var run = PhoneEventStream.RunAsync(output, rig.Hub, Quiet, cts.Token);
        await Wait.Until(() => rig.Hub.SubscriberCount == 1 && output.Text.Contains("event: state"));
        rig.Clock.Advance(TimeSpan.FromSeconds(5));

        rig.Change(Base(rank: 9));
        await Wait.Until(() => Count(output.Text, "event: state\n") == 2);
        cts.Cancel();
        await run;

        var text = output.Text;
        var notice = text.IndexOf("event: notice\n", StringComparison.Ordinal);
        Assert.True(notice > 0);
        Assert.Contains("\"kind\":\"RankUp\"", text);
        Assert.True(notice < text.LastIndexOf("event: state\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_quiet_stream_sends_a_ping()
    {
        using var rig = new Rig();
        var output = new RecordingStream();
        using var cts = new CancellationTokenSource();

        var run = PhoneEventStream.RunAsync(output, rig.Hub, TimeSpan.FromMilliseconds(50), cts.Token);
        await Wait.Until(() => output.Text.Contains("event: ping\ndata: {}\n\n"));
        cts.Cancel();
        await run;

        Assert.StartsWith("event: state\n", output.Text);
    }

    [Fact]
    public async Task Cancelling_ends_the_stream_quietly_and_removes_the_subscription()
    {
        using var rig = new Rig();
        var output = new RecordingStream();
        using var cts = new CancellationTokenSource();
        var run = PhoneEventStream.RunAsync(output, rig.Hub, Quiet, cts.Token);
        await Wait.Until(() => rig.Hub.SubscriberCount == 1);

        cts.Cancel();
        await run;

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(0, rig.Hub.SubscriberCount);
    }

    [Fact]
    public async Task Disposing_the_hub_ends_the_stream_quietly()
    {
        var rig = new Rig();
        var output = new RecordingStream();
        using var cts = new CancellationTokenSource();
        var run = PhoneEventStream.RunAsync(output, rig.Hub, Quiet, cts.Token);
        await Wait.Until(() => rig.Hub.SubscriberCount == 1);

        rig.Dispose();
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_cancellation_from_the_output_while_writing_a_message_is_not_taken_for_a_ping_timeout()
    {
        using var rig = new Rig();
        var output = new FailingAfterFirstFrame();
        using var cts = new CancellationTokenSource();
        var run = PhoneEventStream.RunAsync(output, rig.Hub, Quiet, cts.Token);
        await Wait.Until(() => rig.Hub.SubscriberCount == 1 && output.Text.Contains("event: state"));

        rig.Change(Base(selected: "Bravo"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.DoesNotContain("event: ping", output.Text);
        Assert.Equal(0, rig.Hub.SubscriberCount);
    }
}
