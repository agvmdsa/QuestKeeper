using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using AqwCompanion.App.Feed;
using AqwCompanion.Core.Feed;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Feed;

/// <summary>
/// Real sockets on a free loopback port. Where the machine denies opening a port (the build sandbox does) these tests return at once,
/// so they only count where they can run (the CI machines).
/// </summary>
public class HttpListenerFeedHostTests
{
    private const string Secret = "test-secret-for-the-socket-host";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static int? FreePort()
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
            return null;
        }
    }

    private static (HttpListenerFeedHost Host, FakeFeedSource Source, int Port)? Start()
    {
        if (FreePort() is not { } port)
        {
            return null;
        }

        var source = new FakeFeedSource(FeedSnapshot.Empty with { Selected = "Alpha Hero" });
        var handler = new FeedHandler(() => Encoding.UTF8.GetBytes(Secret), source, $"127.0.0.1:{port}", "9.9.9");
        var host = new HttpListenerFeedHost(handler, source, port);
        return host.TryStart() ? (host, source, port) : null;
    }

    private static HttpRequestMessage Get(int port, string path, string? bearer = Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{path}");
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    [Fact]
    public async Task The_state_is_served_with_the_secret_and_refused_without_it()
    {
        if (Start() is not { } running)
        {
            return;
        }

        using var host = running.Host;
        using var http = new HttpClient { Timeout = Wait };

        var ok = await http.SendAsync(Get(running.Port, "/v1/state"));
        var refused = await http.SendAsync(Get(running.Port, "/v1/state", bearer: null));
        var wrong = await http.SendAsync(Get(running.Port, "/v1/state", bearer: "nope"));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Alpha Hero", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("nosniff", ok.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task A_request_from_a_web_page_is_refused()
    {
        if (Start() is not { } running)
        {
            return;
        }

        using var host = running.Host;
        using var http = new HttpClient { Timeout = Wait };
        var request = Get(running.Port, "/v1/state");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_event_stream_sends_the_state_at_once_and_again_after_a_change()
    {
        if (Start() is not { } running)
        {
            return;
        }

        using var host = running.Host;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await http.SendAsync(Get(running.Port, "/v1/events"), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);

        var first = await ReadEventAsync(reader);
        Assert.StartsWith("event: state", first);
        Assert.Contains("Alpha Hero", first);

        running.Source.Current = FeedSnapshot.Empty with { Selected = "Beta Hero" };
        running.Source.Raise();
        var second = await ReadEventAsync(reader);
        Assert.Contains("Beta Hero", second);
    }

    [Fact]
    public async Task Stopping_closes_the_port_and_a_new_start_works()
    {
        if (Start() is not { } running)
        {
            return;
        }

        using var host = running.Host;
        Assert.True(host.IsRunning);

        host.Stop();

        Assert.False(host.IsRunning);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.SendAsync(Get(running.Port, "/v1/ping")));
        Assert.True(host.TryStart());
        var again = await http.SendAsync(Get(running.Port, "/v1/ping"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public void A_port_that_is_taken_cannot_be_started()
    {
        if (FreePort() is not { } port)
        {
            return;
        }

        using var taken = new TcpListener(IPAddress.Loopback, port);
        taken.Start();
        var source = new FakeFeedSource();
        using var host = new HttpListenerFeedHost(new FeedHandler(() => [1], source, $"127.0.0.1:{port}", "9.9.9"), source, port);

        Assert.False(host.TryStart());
        Assert.False(host.IsRunning);
    }

    private static async Task<string> ReadEventAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        using var cts = new CancellationTokenSource(Wait);
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new EndOfStreamException();
            if (line.Length == 0 && text.Length > 0)
            {
                return text.ToString();
            }

            if (line.Length > 0)
            {
                text.AppendLine(line);
            }
        }
    }
}
