using System.Net;
using System.Text;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Web;
using Serilog;

namespace AqwCompanion.App.Phone;

/// <summary>
/// The sockets around <see cref="PhoneHandler"/>: an <see cref="HttpListener"/> on <c>http://127.0.0.1:{port}/</c> only. All the rules
/// live in the handler; this class moves bytes, drops any peer that is not loopback, caps the body at 4096 characters, refuses a
/// repeated Host or Origin header, allows at most <see cref="MaxStreams"/> event streams, gives an ordinary request 30 seconds and
/// closes the streams of a revoked device. Event streams stay open until the client leaves, the device is revoked or <see cref="Stop"/>.
/// </summary>
public sealed class PhoneHost : IPhoneHost, IDisposable
{
    public const int DefaultPort = 47822;
    public const int MaxStreams = 8;
    public const int MaxBody = 4096;

    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The count of open streams of one start; a new start gets a new one, so a stream of an old start cannot upset it.</summary>
    private sealed class Slots
    {
        public int Open;
    }

    private readonly PhoneHandler _handler;
    private readonly PhoneHub _hub;
    private readonly PhoneRegistry _registry;
    private readonly int _port;
    private readonly object _lock = new();
    private readonly Dictionary<string, List<CancellationTokenSource>> _streams = new(StringComparer.Ordinal);
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Slots _slots = new();
    private int _disposed;

    public PhoneHost(PhoneHandler handler, PhoneHub hub, PhoneRegistry registry, int port = DefaultPort)
    {
        _handler = handler;
        _hub = hub;
        _registry = registry;
        _port = port;
        _registry.DevicesRevoked += OnDevicesRevoked;
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
                Log.Warning("Phone host could not start: {ExceptionType}", ex.GetType().Name);
                listener.Close();
                return false;
            }

            _listener = listener;
            _cts = new CancellationTokenSource();
            _slots = new Slots();
            var token = _cts.Token;
            var slots = _slots;
            _ = Task.Run(() => AcceptLoopAsync(listener, slots, token));
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
            Interlocked.Exchange(ref _slots.Open, 0);
            _streams.Clear();
        }

        // Every stream token is linked to this one, so this ends them all.
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _registry.DevicesRevoked -= OnDevicesRevoked;
        }

        Stop();
    }

    /// <summary>True only for a loopback peer (IPv4-mapped IPv6 counts as its IPv4 address); no endpoint is not loopback.</summary>
    public static bool IsLoopbackPeer(IPEndPoint? endpoint)
    {
        if (endpoint is null)
        {
            return false;
        }

        var address = endpoint.Address.IsIPv4MappedToIPv6 ? endpoint.Address.MapToIPv4() : endpoint.Address;
        return IPAddress.IsLoopback(address);
    }

    private void OnDevicesRevoked(IReadOnlyList<string> ids)
    {
        lock (_lock)
        {
            foreach (var id in ids)
            {
                if (_streams.TryGetValue(id, out var list))
                {
                    // Cancelled under the lock: a stream is disposed only after it left the dictionary under the same lock.
                    foreach (var cts in list)
                    {
                        cts.Cancel();
                    }
                }
            }
        }
    }

    private async Task AcceptLoopAsync(HttpListener listener, Slots slots, CancellationToken ct)
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

            _ = Task.Run(() => ServeAsync(context, slots, ct), ct);
        }
    }

    private async Task ServeAsync(HttpListenerContext context, Slots slots, CancellationToken ct)
    {
        // http.sys listens on every local address even for a 127.0.0.1 prefix; the prefix only routes. Only this machine may talk.
        if (!IsLoopbackPeer(context.Request.RemoteEndPoint))
        {
            TryAbort(context);
            return;
        }

        var slotTaken = false;
        try
        {
            var http = context.Response;
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            requestCts.CancelAfter(RequestTimeout);
            var rct = requestCts.Token;

            if (HasRepeated(context.Request.Headers, "Host") || HasRepeated(context.Request.Headers, "Origin"))
            {
                await WriteErrorAsync(http, 400, "bad request", rct).ConfigureAwait(false);
                return;
            }

            if (context.Request.ContentLength64 > MaxBody)
            {
                await WriteErrorAsync(http, 413, "body too large", rct).ConfigureAwait(false);
                return;
            }

            var body = await ReadBodyAsync(context.Request, rct).ConfigureAwait(false);
            if (body is null)
            {
                await WriteErrorAsync(http, 413, "body too large", rct).ConfigureAwait(false);
                return;
            }

            var response = _handler.Handle(ToPhoneRequest(context.Request, body));
            if (response.IsEventStream)
            {
                // The ninth stream is turned away before anything of the handler's answer is written.
                if (Interlocked.Increment(ref slots.Open) > MaxStreams)
                {
                    Interlocked.Decrement(ref slots.Open);
                    await WriteErrorAsync(http, 503, "too many streams", rct).ConfigureAwait(false);
                    return;
                }

                slotTaken = true;
            }

            http.StatusCode = response.Status;
            http.ContentType = response.ContentType;
            foreach (var (name, value) in response.Headers)
            {
                if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    http.AppendHeader(name, value);
                }
                else
                {
                    http.Headers.Set(name, value);
                }
            }

            if (response.IsEventStream)
            {
                requestCts.CancelAfter(Timeout.InfiniteTimeSpan);
                http.SendChunked = true;
                await RunStreamAsync(http, response.DeviceId, ct).ConfigureAwait(false);
            }
            else if (response.Status is 204 or 304)
            {
                http.ContentLength64 = 0;
            }
            else if (context.Request.HttpMethod.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                // The handler leaves the body of a HEAD empty; the real length is not known here, so none is advertised.
            }
            else
            {
                http.ContentLength64 = response.Body.Length;
                await http.OutputStream.WriteAsync(response.Body, rct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException or ObjectDisposedException)
        {
            // The client left, the request ran out of time or the host is stopping.
        }
        catch (Exception ex)
        {
            // A fault in one request must not take the program down; drop that connection.
            Log.Error("Phone request failed: {ExceptionType}", ex.GetType().Name);
            TryAbort(context);
        }
        finally
        {
            if (slotTaken)
            {
                Interlocked.Decrement(ref slots.Open);
            }

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

    private async Task RunStreamAsync(HttpListenerResponse http, string? deviceId, CancellationToken ct)
    {
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (deviceId is not null)
        {
            lock (_lock)
            {
                if (!_streams.TryGetValue(deviceId, out var list))
                {
                    _streams[deviceId] = list = [];
                }

                list.Add(streamCts);
            }
        }

        try
        {
            // Events are raised after the registry lock, so a revoke can be announced before this stream registered. Look again now.
            if (deviceId is not null && !_registry.List().Any(d => d.Id == deviceId))
            {
                streamCts.Cancel();
            }

            await PhoneEventStream.RunAsync(http.OutputStream, _hub, PingEvery, streamCts.Token).ConfigureAwait(false);
        }
        finally
        {
            if (deviceId is not null)
            {
                lock (_lock)
                {
                    if (_streams.TryGetValue(deviceId, out var list))
                    {
                        list.Remove(streamCts);
                        if (list.Count == 0)
                        {
                            _streams.Remove(deviceId);
                        }
                    }
                }
            }
        }
    }

    private static void TryAbort(HttpListenerContext context)
    {
        try
        {
            context.Response.Abort();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException or InvalidOperationException)
        {
            // Already closed.
        }
    }

    /// <summary>
    /// True when a header came more than once, or once with a comma (which is how http.sys on Windows hands over a repeated header).
    /// The names are matched without regard to case. The managed listener of other platforms keeps only the last copy, so only
    /// Windows, where this app runs, can show a repeat; the logic is tested on the header collection itself.
    /// </summary>
    internal static bool HasRepeated(System.Collections.Specialized.NameValueCollection headers, string name)
    {
        var values = headers.GetValues(name);
        return values is { Length: > 1 } || (values is { Length: 1 } && values[0].Contains(',', StringComparison.Ordinal));
    }

    /// <summary>The body as text, at most <see cref="MaxBody"/> characters; null when it is longer.</summary>
    private static async Task<string?> ReadBodyAsync(HttpListenerRequest request, CancellationToken ct)
    {
        if (!request.HasEntityBody)
        {
            return "";
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var buffer = new char[MaxBody + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(count), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return count > MaxBody ? null : new string(buffer, 0, count);
    }

    private static async Task WriteErrorAsync(HttpListenerResponse http, int status, string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes($"{{\"error\":\"{message}\"}}");
        http.StatusCode = status;
        http.ContentType = "application/json; charset=utf-8";
        http.KeepAlive = false;
        http.Headers.Set("Cache-Control", "no-store");
        http.Headers.Set("X-Content-Type-Options", "nosniff");
        http.ContentLength64 = bytes.Length;
        await http.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static PhoneRequest ToPhoneRequest(HttpListenerRequest request, string body)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.Headers.AllKeys)
        {
            if (key is not null && request.Headers[key] is { } value)
            {
                headers[key] = value;
            }
        }

        return new PhoneRequest(request.HttpMethod, request.RawUrl ?? "/", headers, body);
    }
}
