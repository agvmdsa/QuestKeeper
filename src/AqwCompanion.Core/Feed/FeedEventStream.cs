using System.Text;

namespace AqwCompanion.Core.Feed;

/// <summary>
/// The body of <c>/v1/events</c>: Server-Sent Events. It sends the current state at once, again whenever the snapshot really changed
/// (never twice in a row for equal content, and not more often than the minimum interval), and a ping when nothing else was sent for a
/// while, so a dead connection is noticed. It ends quietly when cancelled or when the client is gone.
/// </summary>
public static class FeedEventStream
{
    public static string Format(string eventName, string json) => $"event: {eventName}\ndata: {json}\n\n";

    public static async Task RunAsync(
        Stream output,
        IFeedSource source,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan minInterval,
        TimeSpan pingEvery,
        CancellationToken ct)
    {
        using var changed = new SemaphoreSlim(0);

        void OnChanged(object? sender, EventArgs e)
        {
            try
            {
                changed.Release();
            }
            catch (ObjectDisposedException)
            {
                // The stream already ended.
            }
        }

        source.Changed += OnChanged;
        try
        {
            var last = FeedJson.Serialize(source.Current);
            await WriteAsync(output, Format("state", last), ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                using var round = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var signal = changed.WaitAsync(round.Token);
                var quiet = delay(pingEvery, round.Token);
                var first = await Task.WhenAny(signal, quiet).ConfigureAwait(false);
                await round.CancelAsync().ConfigureAwait(false);

                if (ReferenceEquals(first, signal) && signal.IsCompletedSuccessfully)
                {
                    while (changed.CurrentCount > 0 && changed.Wait(0))
                    {
                        // Several changes in a row count as one.
                    }

                    var json = FeedJson.Serialize(source.Current);
                    if (!string.Equals(json, last, StringComparison.Ordinal))
                    {
                        last = json;
                        await WriteAsync(output, Format("state", json), ct).ConfigureAwait(false);
                        await delay(minInterval, ct).ConfigureAwait(false);
                    }
                }
                else if (ReferenceEquals(first, quiet) && quiet.IsCompletedSuccessfully)
                {
                    await WriteAsync(output, Format("ping", "{}"), ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Cancelled, or the client is gone: the normal ways for a stream to end.
        }
        finally
        {
            source.Changed -= OnChanged;
        }
    }

    private static async Task WriteAsync(Stream output, string text, CancellationToken ct)
    {
        await output.WriteAsync(Encoding.UTF8.GetBytes(text), ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }
}
