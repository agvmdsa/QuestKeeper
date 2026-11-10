using System.Net;
using System.Net.Sockets;
using System.Text;
using AqwCompanion.App.Phone;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Web;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

/// <summary>
/// Real sockets on a free loopback port. Where the machine denies opening a port (the build sandbox does) <c>TryStart</c> is false and
/// each test returns at once, so these only count where they can run.
/// </summary>
public sealed class PhoneHostTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private readonly int _port = FreePort();
    private readonly PhoneRegistry _registry = new(new MemoryPhoneFileStore(), () => DateTimeOffset.UtcNow);
    private readonly FakeFeedSource _source = new();
    private readonly PhoneHub _hub;
    private readonly PhoneHost _host;
    private readonly HttpClient _client = NewClient();

    public PhoneHostTests()
    {
        _hub = new PhoneHub(_source, new NoticeEngine(), new NoticeLog(50), () => DateTimeOffset.UtcNow);
        var handler = new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == $"127.0.0.1:{_port}", "9.9.9");
        _host = new PhoneHost(handler, _hub, _registry, _port);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
        _hub.Dispose();
    }

    private static int FreePort()
    {
        try
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
        catch (SocketException)
        {
            return 47999;
        }
    }

    private static HttpClient NewClient() =>
        new(new SocketsHttpHandler { UseProxy = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };

    private static CancellationToken Bounded() => new CancellationTokenSource(Limit).Token;

    private string Url(string path) => $"http://127.0.0.1:{_port}{path}";

    private async Task<string> PairAsync()
    {
        var token = _registry.CreatePairingToken();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/pair"))
        {
            Content = new StringContent($"{{\"token\":\"{token}\",\"name\":\"Phone\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Remote", "1");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        return cookie.Split(';')[0];
    }

    private HttpRequestMessage Get(string path, string? cookie = null, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, Url(path));
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return request;
    }

    private async Task<string> RawAsync(string request)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var cts = new CancellationTokenSource(Limit);
        return await reader.ReadToEndAsync(cts.Token);
    }

    private sealed class OpenStream(HttpResponseMessage response, StreamReader reader) : IDisposable
    {
        public HttpResponseMessage Response { get; } = response;
        public StreamReader Reader { get; } = reader;

        public void Dispose()
        {
            Reader.Dispose();
            Response.Dispose();
        }
    }

    private async Task<OpenStream> OpenEventsAsync(string cookie)
    {
        var request = Get("/api/events", cookie);
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        return new OpenStream(response, new StreamReader(stream, Encoding.UTF8));
    }

    [Fact]
    public async Task The_index_is_served()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        using var response = await _client.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Questkeeper</title>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_head_request_never_advertises_a_length()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        using var request = Get("/", method: HttpMethod.Head);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task State_needs_a_cookie_and_pairing_gives_one()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        using (var anonymous = await _client.GetAsync(Url("/api/state")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var cookie = await PairAsync();
        using var request = Get("/api/state", cookie);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Events_stream_state_and_a_later_change_arrives_on_the_same_connection()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        var cookie = await PairAsync();
        using var open = await OpenEventsAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, open.Response.StatusCode);
        using var cts = new CancellationTokenSource(Limit);

        Assert.Equal("event: state", await open.Reader.ReadLineAsync(cts.Token));
        var first = await open.Reader.ReadLineAsync(cts.Token);
        Assert.StartsWith("data:", first, StringComparison.Ordinal);
        Assert.Equal("", await open.Reader.ReadLineAsync(cts.Token));

        _source.Current = FeedSnapshot.Empty with { Selected = "Alpha Hero" };
        _source.Raise();

        Assert.StartsWith("event:", await open.Reader.ReadLineAsync(cts.Token), StringComparison.Ordinal);
        var second = await open.Reader.ReadLineAsync(cts.Token);
        Assert.StartsWith("data:", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal("", await open.Reader.ReadLineAsync(cts.Token));
    }

    [Fact]
    public async Task The_ninth_event_stream_gets_503_and_a_freed_slot_is_reusable()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        var cookie = await PairAsync();
        var open = new List<OpenStream>();
        try
        {
            for (var i = 0; i < PhoneHost.MaxStreams; i++)
            {
                var stream = await OpenEventsAsync(cookie);
                open.Add(stream);
                Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
                Assert.Equal("event: state", await stream.Reader.ReadLineAsync(Bounded()));
            }

            using (var ninth = await _client.SendAsync(Get("/api/events", cookie), HttpCompletionOption.ResponseHeadersRead))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, ninth.StatusCode);
            }

            open[0].Dispose();
            open.RemoveAt(0);

            // The server only learns that a client left when a write to it fails, so keep the hub changing until the slot is free.
            OpenStream? again = null;
            var until = DateTime.UtcNow.AddSeconds(10);
            for (var n = 0; again is null; n++)
            {
                Assert.True(DateTime.UtcNow < until, "The freed slot was never reusable.");
                _source.Current = FeedSnapshot.Empty with { Selected = $"Hero {n}" };
                _source.Raise();
                await Task.Delay(50);
                var candidate = await OpenEventsAsync(cookie);
                if (candidate.Response.StatusCode == HttpStatusCode.OK)
                {
                    again = candidate;
                }
                else
                {
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, candidate.Response.StatusCode);
                    candidate.Dispose();
                }
            }

            open.Add(again);
        }
        finally
        {
            foreach (var stream in open)
            {
                stream.Dispose();
            }
        }
    }

    [Fact]
    public async Task Revoking_a_device_closes_its_open_stream()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        var cookie = await PairAsync();
        using var open = await OpenEventsAsync(cookie);
        Assert.Equal("event: state", await open.Reader.ReadLineAsync(Bounded()));

        Assert.True(_registry.Revoke(_registry.List().Single().Id));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        string? line;
        do
        {
            line = await open.Reader.ReadLineAsync(cts.Token);
        }
        while (line is not null);

        Assert.Null(line);
    }

    [Fact]
    public async Task A_body_over_4096_bytes_gets_413()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/pair"))
        {
            Content = new StringContent(new string('a', 5000), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Remote", "1");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Two_Host_headers_get_400()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        // The managed listener of macOS and Linux keeps only the last copy of a repeated header; http.sys on Windows is the
        // case that matters and the owner's Windows run covers it. The check itself is tested on a header collection below.
        if (!OperatingSystem.IsWindows() || !_host.TryStart())
        {
            return;
        }

        var host = $"127.0.0.1:{_port}";
        var reply = await RawAsync($"GET /api/ping HTTP/1.1\r\nHost: {host}\r\nHost: {host}\r\nConnection: close\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 400", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_Host_headers_with_different_letter_case_get_400()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        // The managed listener of macOS and Linux keeps only the last copy of a repeated header; http.sys on Windows is the
        // case that matters and the owner's Windows run covers it. The check itself is tested on a header collection below.
        if (!OperatingSystem.IsWindows() || !_host.TryStart())
        {
            return;
        }

        var host = $"127.0.0.1:{_port}";
        var reply = await RawAsync($"GET /api/ping HTTP/1.1\r\nhost: {host}\r\nHost: {host}\r\nConnection: close\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 400", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_Origin_headers_get_400()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        // The managed listener of macOS and Linux keeps only the last copy of a repeated header; http.sys on Windows is the
        // case that matters and the owner's Windows run covers it. The check itself is tested on a header collection below.
        if (!OperatingSystem.IsWindows() || !_host.TryStart())
        {
            return;
        }

        var host = $"127.0.0.1:{_port}";
        var reply = await RawAsync($"GET /api/ping HTTP/1.1\r\nHost: {host}\r\nOrigin: http://{host}\r\norigin: http://{host}\r\nConnection: close\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 400", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stop_closes_the_port()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        using (var before = await _client.GetAsync(Url("/")))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        _host.Stop();

        Assert.False(_host.IsRunning);
        using var fresh = NewClient();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => fresh.GetAsync(Url("/")));
    }

    [Fact]
    public async Task Stop_right_after_start_closes_the_port_and_a_new_start_works()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        _host.Stop();

        Assert.False(_host.IsRunning);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => _client.GetAsync(Url("/")));
        Assert.True(_host.TryStart());
        using var again = await _client.GetAsync(Url("/"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Stop_ends_an_open_stream()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        if (!_host.TryStart())
        {
            return;
        }

        var cookie = await PairAsync();
        using var open = await OpenEventsAsync(cookie);
        Assert.Equal("event: state", await open.Reader.ReadLineAsync(Bounded()));

        _host.Stop();

        // The stream ends either with a clean end of the body or with the connection dropped; both mean it is closed.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (await open.Reader.ReadLineAsync(cts.Token) is not null)
            {
            }
        }
        catch (Exception ex) when (ex is HttpIOException or IOException)
        {
            // Dropped connection.
        }
    }

    [Fact]
    public void A_repeated_header_is_found_whatever_the_letter_case()
    {
        var headers = new System.Collections.Specialized.NameValueCollection { { "host", "a" }, { "Host", "a" } };

        Assert.True(PhoneHost.HasRepeated(headers, "Host"));
    }

    [Fact]
    public void A_comma_in_a_single_header_counts_as_repeated()
    {
        var headers = new System.Collections.Specialized.NameValueCollection { { "Origin", "http://a, http://b" } };

        Assert.True(PhoneHost.HasRepeated(headers, "Origin"));
    }

    [Fact]
    public void The_real_header_collection_shows_a_repeat_whatever_the_letter_case()
    {
        var headers = new WebHeaderCollection { { "host", "a" }, { "Host", "a" } };

        Assert.True(PhoneHost.HasRepeated(headers, "Host"));
    }

    [Fact]
    public void The_real_header_collection_shows_a_comma_joined_value_as_repeated()
    {
        var headers = new WebHeaderCollection { { "Origin", "http://a, http://b" } };

        Assert.True(PhoneHost.HasRepeated(headers, "Origin"));
    }

    [Fact]
    public void The_real_header_collection_has_no_repeat_for_single_or_missing_headers()
    {
        var headers = new WebHeaderCollection { { "Host", "127.0.0.1:1" } };

        Assert.False(PhoneHost.HasRepeated(headers, "Host"));
        Assert.False(PhoneHost.HasRepeated(headers, "Origin"));
    }

    [Fact]
    public void A_disposed_host_cannot_be_started()
    {
        _host.Dispose();

        Assert.False(_host.TryStart());
        Assert.False(_host.IsRunning);
    }

    [Fact]
    public async Task A_stalled_request_is_cut_off_and_the_host_still_serves()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        using var quick = NewHost(TimeSpan.FromSeconds(1), out var port);
        if (!quick.TryStart())
        {
            return;
        }

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"POST /api/pair HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nX-Remote: 1\r\nContent-Length: 100\r\n\r\nabc"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[256];
        try
        {
            while (await stream.ReadAsync(buffer, cts.Token) > 0)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            // Reset by the abort: also a cut-off.
        }

        using var ok = await _client.GetAsync($"http://127.0.0.1:{port}/");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task An_event_stream_outlives_the_request_timeout()
    {
        // Binding is denied in some sandboxes; then there is nothing to test here.
        using var quick = NewHost(TimeSpan.FromMilliseconds(300), out var port);
        if (!quick.TryStart())
        {
            return;
        }

        var token = _registry.CreatePairingToken();
        using var pair = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/pair")
        {
            Content = new StringContent($"{{\"token\":\"{token}\",\"name\":\"Phone\"}}", Encoding.UTF8, "application/json"),
        };
        pair.Headers.Add("X-Remote", "1");
        using var paired = await _client.SendAsync(pair);
        var cookie = paired.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/events");
        request.Headers.Add("Cookie", cookie);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8);
        Assert.Equal("event: state", await reader.ReadLineAsync(Bounded()));
        Assert.StartsWith("data:", await reader.ReadLineAsync(Bounded()), StringComparison.Ordinal);
        Assert.Equal("", await reader.ReadLineAsync(Bounded()));

        await Task.Delay(1200);
        _source.Current = FeedSnapshot.Empty with { Selected = "Late Hero" };
        _source.Raise();

        Assert.StartsWith("event:", await reader.ReadLineAsync(Bounded()), StringComparison.Ordinal);
    }

    private PhoneHost NewHost(TimeSpan timeout, out int port)
    {
        port = FreePort();
        var own = port;
        var handler = new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == $"127.0.0.1:{own}", "9.9.9");
        return new PhoneHost(handler, _hub, _registry, port, timeout);
    }

    [Fact]
    public void A_single_header_and_a_missing_one_are_not_repeated()
    {
        var headers = new System.Collections.Specialized.NameValueCollection { { "Host", "127.0.0.1:1" } };

        Assert.False(PhoneHost.HasRepeated(headers, "Host"));
        Assert.False(PhoneHost.HasRepeated(headers, "Origin"));
    }

    [Fact]
    public void No_peer_endpoint_is_not_loopback() => Assert.False(PhoneHost.IsLoopbackPeer(null));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.5.5.5", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.168.1.5", false)]
    [InlineData("10.0.0.2", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:192.168.1.5", false)]
    public void Only_loopback_peers_are_served(string address, bool expected) =>
        Assert.Equal(expected, PhoneHost.IsLoopbackPeer(new IPEndPoint(IPAddress.Parse(address), 50000)));

    [Fact]
    public void The_limits_and_port_are_fixed()
    {
        Assert.Equal(47822, PhoneHost.DefaultPort);
        Assert.Equal(8, PhoneHost.MaxStreams);
    }
}
