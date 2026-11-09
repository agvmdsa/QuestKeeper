using System.Net;
using AqwCompanion.Core.Feed;
using Serilog;

namespace AqwCompanion.App.Feed;

/// <summary>
/// The sockets around <see cref="FeedHandler"/>: an <see cref="HttpListener"/> on <c>http://127.0.0.1:{port}/</c> only. All the rules
/// (host, origin, method, secret, routes) live in the handler; this class only moves bytes. Event streams stay open until the client
/// leaves or <see cref="Stop"/> is called.
/// </summary>
public sealed class HttpListenerFeedHost : ILocalFeedHost, IDisposable
{
    public const int DefaultPort = 47821;

    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(25);

    private readonly FeedHandler _handler;
    private readonly IFeedSource _source;
    private readonly int _port;
    private readonly object _lock = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public HttpListenerFeedHost(FeedHandler handler, IFeedSource source, int port = DefaultPort)
    {
        _handler = handler;
        _source = source;
        _port = port;
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _listener is not null;
            }
        }
    }

    public bool TryStart()
    {
        lock (_lock)
        {
            if (_listener is not null)
            {
                return true;
            }

            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                listener.Start();
            }
            catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException or ObjectDisposedException)
            {
                Log.Warning("Feed could not start: {ExceptionType}", ex.GetType().Name);
                listener.Close();
                return false;
            }

            _listener = listener;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => AcceptLoopAsync(listener, token));
            return true;
        }
    }

    public void Stop()
    {
        HttpListener? listener;
        CancellationTokenSource? cts;
        lock (_lock)
        {
            listener = _listener;
            cts = _cts;
            _listener = null;
            _cts = null;
        }

        cts?.Cancel();
        try
        {
            listener?.Close();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
        {
            // Already closed.
        }

        cts?.Dispose();
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(context, ct), ct);
        }
    }

    private async Task ServeAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            var request = ToFeedRequest(context.Request);
            var response = _handler.Handle(request);
            var http = context.Response;
            http.StatusCode = response.Status;
            http.ContentType = response.ContentType;
            http.Headers["X-Content-Type-Options"] = "nosniff";
            http.Headers["Cache-Control"] = response.IsEventStream ? "no-cache" : "no-store";

            if (response.IsEventStream)
            {
                http.SendChunked = true;
                await FeedEventStream.RunAsync(http.OutputStream, _source, Task.Delay, MinInterval, PingEvery, ct).ConfigureAwait(false);
            }
            else
            {
                http.ContentLength64 = response.Body.Length;
                await http.OutputStream.WriteAsync(response.Body, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException or ObjectDisposedException)
        {
            // The client left or the host is stopping.
        }
        catch (Exception ex)
        {
            Log.Error("Feed request failed: {ExceptionType}", ex.GetType().Name);
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException or InvalidOperationException)
            {
                // Already closed.
            }
        }
    }

    private static FeedRequest ToFeedRequest(HttpListenerRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.Headers.AllKeys)
        {
            if (key is not null && request.Headers[key] is { } value)
            {
                headers[key] = value;
            }
        }

        return new FeedRequest(request.HttpMethod, request.RawUrl ?? "/", headers);
    }
}
