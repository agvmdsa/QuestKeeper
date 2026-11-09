using System.Text;
using AqwCompanion.Core.Feed;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Feed;

public class FeedEventStreamTests
{
    private static readonly TimeSpan Min = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>Collects what is written, safe to read while the loop is still writing.</summary>
    private sealed class CollectingStream : Stream
    {
        private readonly object _lock = new();
        private readonly StringBuilder _text = new();

        public int Flushes { get; private set; }

        public string Text
        {
            get
            {
                lock (_lock)
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
            lock (_lock)
            {
                Flushes++;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                _text.Append(Encoding.UTF8.GetString(buffer, offset, count));
            }
        }
    }

    /// <summary>A client that has left: every write fails.</summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("the client left");
    }

    /// <summary>The minimum-interval wait passes at once; every ping wait is a pending task the test completes by hand.</summary>
    private sealed class Delays
    {
        private readonly object _lock = new();

        public List<TaskCompletionSource> Pings { get; } = [];

        public Task Delay(TimeSpan span, CancellationToken ct)
        {
            if (span == Min)
            {
                return Task.CompletedTask;
            }

            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                Pings.Add(pending);
            }

            return pending.Task.WaitAsync(ct);
        }

        public int PingWaits
        {
            get
            {
                lock (_lock)
                {
                    return Pings.Count;
                }
            }
        }

        public void Fire(int index)
        {
            lock (_lock)
            {
                Pings[index].TrySetResult();
            }
        }
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, $"Timed out waiting for: {what}.");
            await Task.Delay(5);
        }
    }

    private static int Count(string text, string eventName) =>
        text.Split("event: " + eventName + "\n").Length - 1;

    private static FeedSnapshot Named(string selected) => FeedSnapshot.Empty with { Selected = selected };

    [Fact]
    public void An_event_is_the_name_then_the_data_then_a_blank_line()
    {
        Assert.Equal("event: state\ndata: {\"a\":1}\n\n", FeedEventStream.Format("state", "{\"a\":1}"));
    }

    [Fact]
    public async Task The_first_thing_written_is_the_current_state_and_it_is_flushed()
    {
        var output = new CollectingStream();
        var source = new FakeFeedSource(Named("Alpha Hero"));
        using var cts = new CancellationTokenSource();

        var run = FeedEventStream.RunAsync(output, source, new Delays().Delay, Min, PingEvery, cts.Token);
        await Eventually(() => Count(output.Text, "state") == 1, "the first state");

        Assert.StartsWith("event: state\ndata: " + FeedJson.Serialize(source.Current) + "\n\n", output.Text);
        Assert.True(output.Flushes >= 1);
        cts.Cancel();
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task A_changed_snapshot_is_sent_again()
    {
        var output = new CollectingStream();
        var source = new FakeFeedSource(Named("Alpha Hero"));
        using var cts = new CancellationTokenSource();
        var run = FeedEventStream.RunAsync(output, source, new Delays().Delay, Min, PingEvery, cts.Token);
        await Eventually(() => Count(output.Text, "state") == 1, "the first state");

        source.Current = Named("Beta Hero");
        source.Raise();

        await Eventually(() => Count(output.Text, "state") == 2, "the second state");
        Assert.Contains("Beta Hero", output.Text);
        cts.Cancel();
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task An_equal_snapshot_is_not_sent_again()
    {
        var output = new CollectingStream();
        var source = new FakeFeedSource(Named("Alpha Hero"));
        var delays = new Delays();
        using var cts = new CancellationTokenSource();
        var run = FeedEventStream.RunAsync(output, source, delays.Delay, Min, PingEvery, cts.Token);
        await Eventually(() => Count(output.Text, "state") == 1, "the first state");

        source.Current = Named("Alpha Hero");
        source.Raise();
        source.Raise();
        await Task.Delay(100);

        Assert.Equal(1, Count(output.Text, "state"));
        cts.Cancel();
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task A_quiet_stream_sends_a_ping_and_waits_for_the_next_one()
    {
        var output = new CollectingStream();
        var source = new FakeFeedSource();
        var delays = new Delays();
        using var cts = new CancellationTokenSource();
        var run = FeedEventStream.RunAsync(output, source, delays.Delay, Min, PingEvery, cts.Token);
        await Eventually(() => delays.PingWaits >= 1, "the first ping wait");

        delays.Fire(0);

        await Eventually(() => Count(output.Text, "ping") == 1, "the ping");
        Assert.Contains("event: ping\ndata: {}\n\n", output.Text);
        await Eventually(() => delays.PingWaits >= 2, "the next ping wait");
        cts.Cancel();
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task Cancelling_ends_the_stream_quietly_and_stops_listening()
    {
        var output = new CollectingStream();
        var source = new FakeFeedSource();
        using var cts = new CancellationTokenSource();
        var run = FeedEventStream.RunAsync(output, source, new Delays().Delay, Min, PingEvery, cts.Token);
        await Eventually(() => source.Subscribers == 1, "the subscription");

        cts.Cancel();
        await run.WaitAsync(Wait);

        Assert.Equal(0, source.Subscribers);
    }

    [Fact]
    public async Task A_client_that_left_ends_the_stream_quietly_and_stops_listening()
    {
        var source = new FakeFeedSource();

        await FeedEventStream.RunAsync(new ThrowingStream(), source, new Delays().Delay, Min, PingEvery, CancellationToken.None)
            .WaitAsync(Wait);

        Assert.Equal(0, source.Subscribers);
    }
}
