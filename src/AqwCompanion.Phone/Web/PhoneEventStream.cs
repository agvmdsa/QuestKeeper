using System.Text;
using System.Threading.Channels;
using AqwCompanion.Phone.Hub;

namespace AqwCompanion.Phone.Web;

/// <summary>The body of <c>/api/events</c>: the current state at once, then every hub message, and a ping when it is quiet.</summary>
public static class PhoneEventStream
{
    public static string Format(string eventName, string json) => $"event: {eventName}\ndata: {json}\n\n";

    public static async Task RunAsync(Stream output, PhoneHub hub, TimeSpan pingEvery, CancellationToken ct)
    {
        using var subscription = hub.Subscribe();
        try
        {
            await WriteAsync(output, Format("state", hub.CurrentJson), ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(pingEvery);
                HubMessage? message = null;
                try
                {
                    message = await subscription.Reader.ReadAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Only the wait timing out is a ping; a failing write below must not be taken for one.
                }

                var frame = message is null ? Format("ping", "{}") : Format(message.Event, message.Json);
                await WriteAsync(output, frame, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is stopping or the client left.
        }
        catch (ChannelClosedException)
        {
            // The hub is gone.
        }
    }

    private static async Task WriteAsync(Stream output, string frame, CancellationToken ct)
    {
        await output.WriteAsync(Encoding.UTF8.GetBytes(frame), ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }
}
