using System.Text;
using System.Text.Json;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Web;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneHandlerTests : IDisposable
{
    private const string GoodHost = "127.0.0.1:47822";

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 9, 12, 6, 0, TimeSpan.Zero));
    private readonly PhoneRegistry _registry;
    private readonly PhoneHub _hub;
    private readonly FakeAssets _assets = new();
    private readonly PhoneHandler _handler;

    public PhoneHandlerTests()
    {
        _registry = new PhoneRegistry(new MemoryPhoneFileStore(), _clock.GetUtcNow);
        _hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), _clock.GetUtcNow);
        _handler = NewHandler("9.9.9");
    }

    public void Dispose() => _hub.Dispose();

    private PhoneHandler NewHandler(string version) =>
        new(_registry, _hub, _assets, h => h is "127.0.0.1:47822" or "localhost:47822", version);

    private static PhoneRequest Req(string method, string path, (string Name, string Value)[]? headers = null, string body = "", string? host = GoodHost)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (host is not null)
        {
            map["Host"] = host;
        }

        foreach (var (name, value) in headers ?? [])
        {
            map[name] = value;
        }

        return new PhoneRequest(method, path, map, body);
    }

    private string Paired()
    {
        var result = _registry.TryPair(_registry.CreatePairingToken(), "Phone");
        Assert.Equal(PairStatus.Paired, result.Status);
        return result.DeviceToken!;
    }

    private static (string, string) Cookie(string token) => ("Cookie", $"qkp_device={token}");

    private static string Header(PhoneResponse response, string name) =>
        response.Headers.Single(h => h.Key == name).Value;

    private static string Text(PhoneResponse response) => Encoding.UTF8.GetString(response.Body);

    private static (string, string) Write => ("X-Remote", "1");

    private PhoneResponse Pair(string token) =>
        _handler.Handle(Req("POST", "/api/pair", [Write], $"{{\"token\":\"{token}\",\"name\":\"Phone\"}}"));

    [Fact]
    public void The_cookie_is_named_qkp_device()
    {
        Assert.Equal("qkp_device", PhoneHandler.CookieName);
    }

    [Fact]
    public void A_wrong_or_missing_host_is_421_everywhere()
    {
        var token = Paired();

        Assert.Equal(421, _handler.Handle(Req("GET", "/", host: "evil.example")).Status);
        Assert.Equal(421, _handler.Handle(Req("HEAD", "/", host: "evil.example")).Status);
        Assert.Equal(421, _handler.Handle(Req("GET", "/api/state", host: "evil.example")).Status);
        Assert.Equal(421, _handler.Handle(Req("GET", "/api/state", [Cookie(token)], host: "evil.example")).Status);
        Assert.Equal(421, _handler.Handle(Req("GET", "/", host: null)).Status);
        Assert.Equal(421, _handler.Handle(Req("GET", "/api/state", [Cookie(token)], host: null)).Status);
    }

    [Fact]
    public void The_index_is_served_with_its_security_headers()
    {
        var response = _handler.Handle(Req("GET", "/"));

        Assert.Equal(200, response.Status);
        Assert.Equal(_assets.Items["/index.html"].Bytes, response.Body);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.Equal("\"abc\"", Header(response, "ETag"));
        Assert.Equal("no-cache", Header(response, "Cache-Control"));
        Assert.Equal(
            "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            Header(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.False(response.IsEventStream);
        Assert.Null(response.DeviceId);
    }

    [Fact]
    public void Static_files_follow_the_conditional_and_path_rules()
    {
        var notModified = _handler.Handle(Req("GET", "/", [("If-None-Match", "\"abc\"")]));
        Assert.Equal(304, notModified.Status);
        Assert.Empty(notModified.Body);

        var script = _handler.Handle(Req("GET", "/js/main.js"));
        Assert.Equal(200, script.Status);
        Assert.Equal("text/javascript; charset=utf-8", script.ContentType);
        Assert.Equal(_assets.Items["/js/main.js"].Bytes, script.Body);

        Assert.Equal(404, _handler.Handle(Req("GET", "/nope.js")).Status);
        Assert.Equal(404, _handler.Handle(Req("GET", "/../secret")).Status);
        Assert.Equal(404, _handler.Handle(Req("GET", "/a\\b")).Status);
        Assert.Equal(405, _handler.Handle(Req("POST", "/")).Status);
        Assert.Equal(405, _handler.Handle(Req("PUT", "/")).Status);
        Assert.Equal(405, _handler.Handle(Req("DELETE", "/js/main.js")).Status);
        Assert.Equal(405, _handler.Handle(Req("OPTIONS", "/")).Status);
    }

    [Fact]
    public void A_query_string_on_a_static_path_is_ignored()
    {
        var plain = _handler.Handle(Req("GET", "/js/main.js"));
        var withQuery = _handler.Handle(Req("GET", "/js/main.js?v=1&x=../../etc"));
        var index = _handler.Handle(Req("GET", "/?pairing=abc"));

        Assert.Equal(200, withQuery.Status);
        Assert.Equal(plain.Body, withQuery.Body);
        Assert.Equal(200, index.Status);
        Assert.Equal(_assets.Items["/index.html"].Bytes, index.Body);
        Assert.Equal(404, _handler.Handle(Req("GET", "/nope.js?/js/main.js")).Status);
    }

    [Fact]
    public void Head_on_a_static_path_has_the_get_headers_and_no_body()
    {
        var get = _handler.Handle(Req("GET", "/"));
        var head = _handler.Handle(Req("HEAD", "/"));

        Assert.Equal(200, head.Status);
        Assert.Empty(head.Body);
        Assert.Equal(get.ContentType, head.ContentType);
        Assert.Equal(get.Headers, head.Headers);
        Assert.Equal(304, _handler.Handle(Req("head", "/", [("If-None-Match", "\"abc\"")])).Status);
        Assert.Equal(404, _handler.Handle(Req("HEAD", "/nope.js")).Status);
    }

    [Fact]
    public void Head_on_an_api_path_is_405_and_never_reaches_the_state()
    {
        var token = Paired();

        var head = _handler.Handle(Req("HEAD", "/api/state", [Cookie(token), Write]));

        Assert.Equal(405, head.Status);
        Assert.DoesNotContain("\"snapshot\"", Text(head));
        Assert.Equal(403, _handler.Handle(Req("HEAD", "/api/state", [Cookie(token)])).Status);
        Assert.Equal(405, _handler.Handle(Req("HEAD", "/api/events", [Cookie(token), Write])).Status);
    }

    [Fact]
    public void The_state_needs_the_right_device_cookie()
    {
        var token = Paired();

        Assert.Equal(401, _handler.Handle(Req("GET", "/api/state")).Status);
        Assert.Equal(401, _handler.Handle(Req("GET", "/api/state", [Cookie("wrong")])).Status);

        var ok = _handler.Handle(Req("GET", "/api/state", [Cookie(token)]));
        Assert.Equal(200, ok.Status);
        Assert.Equal(_hub.CurrentJson, Text(ok));
        Assert.Equal("no-store", Header(ok, "Cache-Control"));

        var among = _handler.Handle(Req("GET", "/api/state", [("Cookie", $"a=1; qkp_device={token}; b=2")]));
        Assert.Equal(200, among.Status);
    }

    [Fact]
    public void A_cookie_with_a_longer_name_ending_in_ours_does_not_authenticate()
    {
        var token = Paired();

        Assert.Equal(401, _handler.Handle(Req("GET", "/api/state", [("Cookie", $"xqkp_device={token}")])).Status);
        Assert.Equal(401, _handler.Handle(Req("GET", "/api/state", [("Cookie", $"a=1; xqkp_device={token}")])).Status);
        Assert.Equal(401, _handler.Handle(Req("GET", "/api/state", [("Cookie", $"qkp_device_x={token}")])).Status);
        Assert.Equal(401, _handler.Handle(Req("GET", "/api/state", [("Cookie", $"QKP_DEVICE={token}")])).Status);
    }

    [Fact]
    public void With_duplicate_cookies_the_first_qkp_device_decides()
    {
        var token = Paired();

        Assert.Equal(200, _handler.Handle(Req("GET", "/api/state", [("Cookie", $"qkp_device={token}; qkp_device=wrong")])).Status);
        Assert.Equal(401, _handler.Handle(Req("GET", "/api/state", [("Cookie", $"qkp_device=wrong; qkp_device={token}")])).Status);
    }

    [Fact]
    public void A_foreign_origin_is_refused_and_the_same_origin_is_accepted()
    {
        var token = Paired();

        var foreign = _handler.Handle(Req("GET", "/api/state", [Cookie(token), ("Origin", "https://evil.example")]));
        Assert.Equal(403, foreign.Status);

        var same = _handler.Handle(Req("GET", "/api/state", [Cookie(token), ("Origin", "http://127.0.0.1:47822")]));
        Assert.Equal(200, same.Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("http://127.0.0.1:47822.evil.example")]
    [InlineData("http://evil.example/127.0.0.1:47822")]
    [InlineData("http://127.0.0.1:47822/")]
    [InlineData("file://")]
    public void An_origin_that_is_not_exactly_ours_is_403(string origin)
    {
        var token = Paired();

        Assert.Equal(403, _handler.Handle(Req("GET", "/api/state", [Cookie(token), ("Origin", origin)])).Status);
        Assert.Equal(403, _handler.Handle(Req("POST", "/api/pair", [Write, ("Origin", origin)], "{}")).Status);
    }

    [Fact]
    public void Pairing_sets_a_locked_down_cookie_and_the_token_works_once()
    {
        var pairingToken = _registry.CreatePairingToken();

        var noHeader = _handler.Handle(Req("POST", "/api/pair", body: $"{{\"token\":\"{pairingToken}\",\"name\":\"Phone\"}}"));
        Assert.Equal(403, noHeader.Status);

        Assert.Equal(401, Pair("wrong").Status);

        var paired = Pair(pairingToken);
        Assert.Equal(204, paired.Status);
        var setCookie = Header(paired, "Set-Cookie");
        Assert.StartsWith("qkp_device=", setCookie);
        Assert.Contains("HttpOnly", setCookie);
        Assert.Contains("Secure", setCookie);
        Assert.Contains("SameSite=Strict", setCookie);
        Assert.Contains("Path=/", setCookie);
        Assert.Equal("no-store", Header(paired, "Cache-Control"));

        var deviceToken = setCookie.Split(';')[0]["qkp_device=".Length..];
        Assert.Equal(200, _handler.Handle(Req("GET", "/api/state", [Cookie(deviceToken)])).Status);

        Assert.Equal(401, Pair(pairingToken).Status);
        Assert.Equal(401, _handler.Handle(Req("POST", "/api/pair", [Write], "{not json")).Status);
    }

    [Fact]
    public void Five_wrong_attempts_lock_pairing_even_for_a_good_token_and_the_429_is_fixed()
    {
        var good = _registry.CreatePairingToken();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(401, Pair("wrong" + i).Status);
        }

        var locked = Pair(good);

        Assert.Equal(429, locked.Status);
        Assert.Equal("60", Header(locked, "Retry-After"));
        Assert.Equal("{\"error\":\"too many attempts\"}", Text(locked));
        Assert.DoesNotContain(good, Text(locked));
        Assert.DoesNotContain(locked.Headers, h => h.Key == "Set-Cookie");
    }

    [Fact]
    public void Other_methods_are_405()
    {
        var token = Paired();

        Assert.Equal(405, _handler.Handle(Req("DELETE", "/api/state", [Cookie(token), Write])).Status);
        Assert.Equal(405, _handler.Handle(Req("POST", "/api/state", [Cookie(token), Write])).Status);
        Assert.Equal(405, _handler.Handle(Req("GET", "/api/pair")).Status);
    }

    [Fact]
    public void Ping_events_and_unknown_routes()
    {
        var token = Paired();

        var ping = _handler.Handle(Req("GET", "/api/ping", [Cookie(token)]));
        Assert.Equal(200, ping.Status);
        Assert.Equal("{\"app\":\"Questkeeper\",\"version\":\"9.9.9\"}", Text(ping));

        var events = _handler.Handle(Req("GET", "/api/events", [Cookie(token)]));
        Assert.True(events.IsEventStream);
        Assert.Equal("text/event-stream", events.ContentType);

        Assert.Equal(404, _handler.Handle(Req("GET", "/api/other", [Cookie(token)])).Status);
    }

    [Fact]
    public void The_ping_version_is_json_escaped()
    {
        const string version = "1.0\"},\"app\":\"evil\\\n";
        var token = Paired();

        var ping = NewHandler(version).Handle(Req("GET", "/api/ping", [Cookie(token)]));

        using var json = JsonDocument.Parse(Text(ping));
        Assert.Equal("Questkeeper", json.RootElement.GetProperty("app").GetString());
        Assert.Equal(version, json.RootElement.GetProperty("version").GetString());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void The_event_stream_is_no_store_and_names_the_device()
    {
        var token = Paired();
        var id = Assert.Single(_registry.List()).Id;

        var events = _handler.Handle(Req("GET", "/api/events", [Cookie(token)]));

        Assert.Equal(200, events.Status);
        Assert.Equal("no-store", Header(events, "Cache-Control"));
        Assert.Equal("nosniff", Header(events, "X-Content-Type-Options"));
        Assert.Equal(id, events.DeviceId);
        Assert.Empty(events.Body);
    }

    [Fact]
    public void The_device_id_is_set_only_for_the_event_stream()
    {
        var token = Paired();

        Assert.Null(_handler.Handle(Req("GET", "/api/state", [Cookie(token)])).DeviceId);
        Assert.Null(_handler.Handle(Req("GET", "/api/ping", [Cookie(token)])).DeviceId);
        Assert.Null(_handler.Handle(Req("GET", "/api/events")).DeviceId);
        Assert.Null(_handler.Handle(Req("GET", "/api/events", [Cookie("wrong")])).DeviceId);
    }

    [Fact]
    public void A_revoked_device_is_401_again()
    {
        var token = Paired();
        _registry.RevokeAll();

        Assert.Equal(401, _handler.Handle(Req("GET", "/api/events", [Cookie(token)])).Status);
    }

    [Fact]
    public void Error_bodies_never_echo_the_tokens()
    {
        var deviceToken = Paired();
        var pairingToken = _registry.CreatePairingToken();
        var responses = new List<PhoneResponse>
        {
            _handler.Handle(Req("GET", "/api/state", [Cookie(deviceToken + "x")])),
            _handler.Handle(Req("GET", "/api/state", [Cookie(deviceToken), ("Origin", "https://evil.example")])),
            _handler.Handle(Req("GET", "/api/nope", [Cookie(deviceToken)])),
            _handler.Handle(Req("POST", "/api/state", [Cookie(deviceToken), Write])),
            _handler.Handle(Req("POST", "/api/pair", body: $"{{\"token\":\"{pairingToken}\"}}")),
            _handler.Handle(Req("GET", "/api/state", [Cookie(deviceToken)], host: "evil.example")),
            _handler.Handle(Req("GET", "/" + pairingToken)),
            Pair(pairingToken + "x"),
        };

        foreach (var response in responses.Where(r => r.Status >= 400))
        {
            var body = Text(response);
            Assert.DoesNotContain(deviceToken, body);
            Assert.DoesNotContain(pairingToken, body);
            Assert.Contains("\"error\":", body);
        }

        Assert.All(responses, r => Assert.True(r.Status >= 400));
    }
}
