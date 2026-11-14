using System.Globalization;
using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Phone.Web;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public sealed class SkuaApiTests : IDisposable
{
    private const string GoodHost = "127.0.0.1:47822";
    private const string Sid = "0123456789abcdef";
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new(T0);
    private readonly PhoneRegistry _registry;
    private readonly PhoneHub _hub;
    private readonly FakeSkua _skua = new();
    private readonly MemorySkuaScriptFile _prefsFile = new();
    private readonly MemorySkuaScriptFile _optionsFile = new();
    private readonly SkuaScriptPrefs _prefs;
    private readonly SkuaOptionCache _cache;
    private readonly PhoneHandler _handler;
    private readonly string _token;

    public SkuaApiTests()
    {
        _registry = new PhoneRegistry(new MemoryPhoneFileStore(), _clock.GetUtcNow);
        _hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), _clock.GetUtcNow);
        _prefs = new SkuaScriptPrefs(_prefsFile, _clock.GetUtcNow);
        _cache = new SkuaOptionCache(_optionsFile, _clock.GetUtcNow);
        var api = new SkuaApi(_skua, _skua, _prefs, _cache, CultureInfo.GetCultureInfo("de-DE"));
        _handler = new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == GoodHost, "9.9.9", api);
        _token = _registry.TryPair(_registry.CreatePairingToken(), "Phone").DeviceToken!;
        _skua.Current = new SkuaHubView([FakeSkua.Instance("a1", "Hero"), FakeSkua.Instance("b2", "Zed", disconnected: true)]);
    }

    public void Dispose() => _hub.Dispose();

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private static readonly string[] InstanceRoutesGet = ["/api/skua/a1/scripts", $"/api/skua/a1/scripts/{Sid}/options"];

    private static readonly (string Path, string Body)[] InstanceRoutesPost =
    [
        ($"/api/skua/a1/scripts/{Sid}/options", "{\"values\":[]}"),
        ("/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}"),
        ("/api/skua/a1/stop", "{}"),
        ($"/api/skua/a1/scripts/{Sid}/prefs", "{\"askOptions\":false}"),
    ];

    private PhoneResponse Get(string path, bool cookie = true) => _handler.Handle(Request("GET", path, "", null, cookie, write: false));

    private PhoneResponse Post(string path, string body, string? contentType = "application/json", bool cookie = true, bool write = true, string? origin = null) =>
        _handler.Handle(Request("POST", path, body, contentType, cookie, write, origin));

    private PhoneRequest Request(string method, string path, string body, string? contentType, bool cookie, bool write, string? origin = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Host"] = GoodHost };
        if (cookie)
        {
            headers["Cookie"] = $"qkp_device={_token}";
        }

        if (write)
        {
            headers["X-Remote"] = "1";
        }

        if (contentType is not null)
        {
            headers["Content-Type"] = contentType;
        }

        if (origin is not null)
        {
            headers["Origin"] = origin;
        }

        return new PhoneRequest(method, path, headers, body);
    }

    private static string Text(PhoneResponse response) => Encoding.UTF8.GetString(response.Body);

    private static string ErrorOf(PhoneResponse response)
    {
        using var doc = JsonDocument.Parse(Text(response));
        Assert.Single(doc.RootElement.EnumerateObject());
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    private static ResultMessage Ok(
        IReadOnlyList<SkuaScriptInfo>? scripts = null, IReadOnlyList<SkuaOptionDef>? defs = null, IReadOnlyList<SkuaOptionValue>? values = null,
        bool hasOptions = false, bool firstRun = false, bool truncated = false) =>
        new("x", true, null, scripts, defs, values, hasOptions, firstRun, truncated);

    private static readonly SkuaOptionDef Rate = new("Options", "Rate", "Drop rate", "How fast", "double", "1.5");
    private static readonly SkuaOptionDef Mode = new("Options", "Mode", "Mode", "", "enum", "Fast Farm", ["Fast Farm", "Slow"]);
    private static readonly SkuaOptionDef Follow = new("Options", "Follow", "Player to follow", "", "string", "");
    private static readonly SkuaOptionDef Loop = new("Extra", "Loop", "Loop", "", "bool", "False");
    private static readonly IReadOnlyList<SkuaOptionDef> Defs = [Rate, Mode, Follow, Loop];

    // ---- authentication and request rules ---------------------------------------------------------------------------

    [Fact]
    public void Every_route_needs_the_device_cookie_and_sends_nothing_without_it()
    {
        Assert.Equal(401, Get("/api/skua", cookie: false).Status);
        foreach (var path in InstanceRoutesGet)
        {
            Assert.Equal(401, Get(path, cookie: false).Status);
        }

        foreach (var (path, body) in InstanceRoutesPost)
        {
            Assert.Equal(401, Post(path, body, cookie: false).Status);
        }

        Assert.Empty(_skua.Sent);
    }

    [Fact]
    public void Posts_from_another_origin_or_without_the_write_header_are_403()
    {
        foreach (var (path, body) in InstanceRoutesPost)
        {
            Assert.Equal("forbidden origin", ErrorOf(Post(path, body, origin: "https://evil.example")));
            Assert.Equal(403, Post(path, body, origin: "https://evil.example").Status);
            Assert.Equal(403, Post(path, body, write: false).Status);
            Assert.Equal(200, Post(path, body, origin: $"https://{GoodHost}").Status);
        }

        Assert.Equal(403, _handler.Handle(Request("GET", "/api/skua", "", null, cookie: true, write: false, origin: "https://evil.example")).Status);
    }

    [Fact]
    public void A_body_that_is_not_json_is_415_and_a_charset_is_fine()
    {
        foreach (var (path, body) in InstanceRoutesPost)
        {
            var response = Post(path, body, "text/plain");
            Assert.Equal(415, response.Status);
            Assert.Equal("body must be json", ErrorOf(response));
            Assert.Equal(415, Post(path, body, contentType: null).Status);
            Assert.Equal(415, Post(path, body, "application/jsonx").Status);
            Assert.Equal(200, Post(path, body, "Application/JSON; charset=utf-8").Status);
        }

        Assert.DoesNotContain(_skua.Sent, s => s.Command is StartCommand && s.InstanceId != "a1");
    }

    [Fact]
    public void Stop_takes_an_empty_body_without_a_content_type()
    {
        var response = Post("/api/skua/a1/stop", "", contentType: null);

        Assert.Equal(200, response.Status);
        Assert.Equal("{\"ok\":true}", Text(response));
        var sent = Assert.Single(_skua.Sent);
        Assert.Equal("a1", sent.InstanceId);
        Assert.IsType<StopCommand>(sent.Command);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"id\"")]
    [InlineData("{")]
    [InlineData("{\"id\":1}")]
    [InlineData("{} {}")]
    public void A_malformed_body_is_400(string body)
    {
        var response = Post("/api/skua/a1/start", body);

        Assert.Equal(400, response.Status);
        Assert.Equal("invalid body", ErrorOf(response));
        Assert.Empty(_skua.Sent);
    }

    [Fact]
    public void Bodies_over_1_kb_and_options_bodies_over_4_kb_are_413()
    {
        var big = $"{{\"id\":\"{Sid}\",\"pad\":\"{new string('a', 1024)}\"}}";
        Assert.Equal(413, Post("/api/skua/a1/start", big).Status);
        Assert.Equal("body too large", ErrorOf(Post("/api/skua/a1/stop", big)));
        Assert.Equal(413, Post($"/api/skua/a1/scripts/{Sid}/prefs", big).Status);

        // 4 KB in UTF-8 bytes, not characters.
        var options = $"{{\"values\":[],\"pad\":\"{new string('\u00e9', 2100)}\"}}";
        Assert.Equal(413, Post($"/api/skua/a1/scripts/{Sid}/options", options).Status);
        var fits = $"{{\"values\":[],\"pad\":\"{new string('a', 3000)}\"}}";
        Assert.Equal(200, Post($"/api/skua/a1/scripts/{Sid}/options", fits).Status);
        Assert.DoesNotContain(_skua.Sent, s => s.Command is StartCommand or StopCommand);
    }

    [Fact]
    public void Wrong_methods_are_405_and_unknown_routes_404()
    {
        Assert.Equal(405, Post("/api/skua", "{}").Status);
        Assert.Equal(405, Get("/api/skua/a1/start").Status);
        Assert.Equal(405, Get("/api/skua/a1/stop").Status);
        Assert.Equal(405, Get($"/api/skua/a1/scripts/{Sid}/prefs").Status);
        Assert.Equal(405, Post("/api/skua/a1/scripts", "{}").Status);
        Assert.Equal(405, _handler.Handle(Request("DELETE", $"/api/skua/a1/scripts/{Sid}/options", "", null, true, true)).Status);
        Assert.Equal(404, Get("/api/skua/").Status);
        Assert.Equal(404, Get("/api/skua/a1").Status);
        Assert.Equal(404, Get("/api/skua/a1/scripts/").Status);
        Assert.Equal(404, Get($"/api/skua/a1/scripts/{Sid}").Status);
        Assert.Equal(404, Get("/api/skuax").Status);
        Assert.Equal(404, Post("/api/skua/a1/restart", "{}").Status);
        Assert.Empty(_skua.Sent);
    }

    // ---- instances ----------------------------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_instance_is_404_and_a_disconnected_one_409_on_every_route()
    {
        foreach (var path in InstanceRoutesGet)
        {
            Assert.Equal("unknown Skua", ErrorOf(Get(path.Replace("/a1/", "/zz/", StringComparison.Ordinal))));
            Assert.Equal(404, Get(path.Replace("/a1/", "/zz/", StringComparison.Ordinal)).Status);
            Assert.Equal(409, Get(path.Replace("/a1/", "/b2/", StringComparison.Ordinal)).Status);
            Assert.Equal("Skua is disconnected", ErrorOf(Get(path.Replace("/a1/", "/b2/", StringComparison.Ordinal))));
        }

        foreach (var (path, body) in InstanceRoutesPost)
        {
            Assert.Equal(404, Post(path.Replace("/a1/", "/zz/", StringComparison.Ordinal), body).Status);
            Assert.Equal(409, Post(path.Replace("/a1/", "/b2/", StringComparison.Ordinal), body).Status);
        }

        Assert.Empty(_skua.Sent);
    }

    [Fact]
    public void An_instance_that_goes_away_while_sending_is_404_or_409()
    {
        _skua.Answer = (_, _) => throw new KeyNotFoundException("unknown Skua");
        Assert.Equal(404, Post("/api/skua/a1/stop", "").Status);

        _skua.Answer = (_, _) => throw new InvalidOperationException("Skua is disconnected");
        Assert.Equal(409, Post("/api/skua/a1/stop", "").Status);
    }

    [Fact]
    public void A_percent_encoded_instance_id_is_matched_decoded()
    {
        _skua.Current = new SkuaHubView([FakeSkua.Instance("a b", "Hero")]);

        Assert.Equal(200, Post("/api/skua/a%20b/stop", "").Status);
        Assert.Equal("a b", Assert.Single(_skua.Sent).InstanceId);
    }

    [Fact]
    public void No_answer_is_504_skua_did_not_answer_on_every_command_route()
    {
        _skua.Answer = (_, _) => Task.FromResult<ResultMessage?>(null);

        foreach (var path in InstanceRoutesGet)
        {
            Assert.Equal(504, Get(path).Status);
            Assert.Equal("Skua did not answer", ErrorOf(Get(path)));
        }

        foreach (var (path, body) in InstanceRoutesPost.Where(r => !r.Path.EndsWith("/prefs", StringComparison.Ordinal)))
        {
            Assert.Equal(504, Post(path, body).Status);
        }
    }

    [Fact]
    public void A_refusal_by_the_plugin_is_400_with_its_text_json_escaped()
    {
        const string hostile = "CS1002: ; expected \"<script>\"\n\\ end";
        _skua.AnswerWith(new ResultMessage("x", false, hostile, null, null, null, false, false));

        var response = Post("/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}");

        Assert.Equal(400, response.Status);
        Assert.Equal(hostile, ErrorOf(response));
        Assert.DoesNotContain("<script>", Text(response));

        _skua.AnswerWith(new ResultMessage("x", false, null, null, null, null, false, false));
        Assert.Equal("Skua refused the command", ErrorOf(Post("/api/skua/a1/stop", "")));
        _skua.AnswerWith(new ResultMessage("x", false, "settling", null, null, null, false, false));
        Assert.Equal("settling", ErrorOf(Post("/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}")));
    }

    [Theory]
    [InlineData("../../x.cs")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdef0")]
    [InlineData("0123456789abcdeg")]
    [InlineData("C:\\\\Scripts\\\\Farm.cs")]
    [InlineData("")]
    public void A_hostile_script_id_in_the_start_body_is_400_and_never_sent(string id)
    {
        var response = Post("/api/skua/a1/start", $"{{\"id\":\"{id}\"}}");

        Assert.Equal(400, response.Status);
        Assert.Equal("invalid script id", ErrorOf(response));
        Assert.Empty(_skua.Sent);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..%2F..%2Fx")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcdef0")]
    [InlineData("%30123456789abcdef")]
    public void A_hostile_script_id_in_the_path_is_400_and_never_sent(string id)
    {
        Assert.Equal(400, Get($"/api/skua/a1/scripts/{id}/options").Status);
        Assert.Equal(400, Post($"/api/skua/a1/scripts/{id}/options", "{\"values\":[]}").Status);
        Assert.Equal("invalid script id", ErrorOf(Post($"/api/skua/a1/scripts/{id}/prefs", "{\"askOptions\":true}")));
        Assert.Empty(_skua.Sent);
        Assert.Null(_prefsFile.Text);
    }

    [Fact]
    public void A_script_id_with_a_trailing_newline_is_400_in_the_body_and_in_the_path()
    {
        Assert.Equal("invalid script id", ErrorOf(Post("/api/skua/a1/start", $"{{\"id\":\"{Sid}\\n\"}}")));
        Assert.Equal(400, Get($"/api/skua/a1/scripts/{Sid}%0A/options").Status);
        Assert.Equal(400, Get($"/api/skua/a1/scripts/{Sid}\n/options").Status);
        Assert.Equal(400, Post($"/api/skua/a1/scripts/{Sid}\n/prefs", "{\"askOptions\":false}").Status);
        Assert.Empty(_skua.Sent);
        Assert.Null(_prefsFile.Text);
    }

    // ---- one state-changing command at a time ----------------------------------------------------------------------------

    [Fact]
    public async Task A_second_state_changing_command_while_one_is_pending_is_409_busy_and_sends_nothing()
    {
        var pending = new TaskCompletionSource<ResultMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _skua.Answer = (_, c) => c is StartCommand ? pending.Task : Task.FromResult<ResultMessage?>(Ok() with { ReqId = c.ReqId });
        var api = new SkuaApi(_skua, _skua, _prefs, _cache);
        var handler = new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == GoodHost, "9.9.9", api);

        var first = handler.HandleAsync(Request("POST", "/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}", "application/json", true, true), CancellationToken.None);
        Assert.False(first.IsCompleted);

        foreach (var (path, body) in new[] { ("/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}"), ("/api/skua/a1/stop", ""), ($"/api/skua/a1/scripts/{Sid}/options", "{\"values\":[]}") })
        {
            var busy = await handler.HandleAsync(Request("POST", path, body, "application/json", true, true), CancellationToken.None);
            Assert.Equal(409, busy.Status);
            Assert.Equal("busy", ErrorOf(busy));
        }

        Assert.Single(_skua.Sent);

        // Read-only routes and other instances are not gated.
        Assert.Equal(200, (await handler.HandleAsync(Request("GET", "/api/skua/a1/scripts", "", null, true, false), CancellationToken.None)).Status);
        _skua.Current = new SkuaHubView([.. _skua.Current.Instances, FakeSkua.Instance("c3", "Other")]);
        Assert.Equal(200, (await handler.HandleAsync(Request("POST", "/api/skua/c3/stop", "", null, true, true), CancellationToken.None)).Status);

        pending.SetResult(Ok());
        Assert.Equal(200, (await first).Status);
        Assert.Equal(200, (await handler.HandleAsync(Request("POST", "/api/skua/a1/stop", "", null, true, true), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task The_gate_opens_again_after_a_timeout_a_refusal_or_a_cancellation()
    {
        var api = new SkuaApi(_skua, _skua, _prefs, _cache);
        var handler = new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == GoodHost, "9.9.9", api);
        PhoneRequest Stop() => Request("POST", "/api/skua/a1/stop", "", null, true, true);

        _skua.Answer = (_, _) => Task.FromResult<ResultMessage?>(null);
        Assert.Equal(504, (await handler.HandleAsync(Stop(), CancellationToken.None)).Status);

        _skua.Answer = (_, _) => Task.FromResult<ResultMessage?>(new ResultMessage("x", false, "not running", null, null, null, false, false));
        Assert.Equal(400, (await handler.HandleAsync(Stop(), CancellationToken.None)).Status);

        using var cts = new CancellationTokenSource();
        var late = new TaskCompletionSource<ResultMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _skua.Answer = (_, _) => late.Task.WaitAsync(cts.Token);
        var cancelled = handler.HandleAsync(Stop(), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(Ok() with { ReqId = c.ReqId });
        Assert.Equal(200, (await handler.HandleAsync(Stop(), CancellationToken.None)).Status);
    }

    // ---- GET /api/skua --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_instance_list_has_every_instance_with_its_status_and_at_most_100_log_lines()
    {
        var log = Enumerable.Range(0, 150).Select(i => new SkuaLogEntry(T0.AddSeconds(-i), SkuaLogKind.Sold, $"Sold {i}")).ToList();
        var status = new SkuaStatus(true, "Hero", null, "battleon", "Farm.cs", true, null, null, [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)], "settling", 7, true);
        _skua.Current = new SkuaHubView([FakeSkua.Instance("a1", "Hero", status: status, log: log), FakeSkua.Instance("b2", "Skua 2", disconnected: true)]);

        var response = Get("/api/skua");
        var json = Text(response);

        Assert.Equal(200, response.Status);
        Assert.Equal("application/json; charset=utf-8", response.ContentType);
        Assert.StartsWith(
            "{\"instances\":[{\"instanceId\":\"a1\",\"label\":\"Hero\",\"connection\":\"Connected\",\"disconnected\":false,\"hello\":null,"
            + "\"status\":{\"loggedIn\":true,\"character\":\"Hero\",\"server\":null,\"map\":\"battleon\",\"script\":\"Farm.cs\",\"scriptRunning\":true,"
            + "\"runningSince\":null,\"quests\":null,\"factions\":[{\"id\":1,\"name\":\"Nation\",\"rank\":3,\"totalRep\":5000,\"rep\":300,\"requiredRep\":1200}],"
            + "\"control\":\"settling\",\"settleSecondsLeft\":7,\"optionsStuck\":true},"
            + "\"log\":[{\"at\":\"2026-11-13T12:00:00+00:00\",\"kind\":\"Sold\",\"text\":\"Sold 0\",\"item\":null,\"count\":1},",
            json);
        Assert.EndsWith(
            "{\"instanceId\":\"b2\",\"label\":\"Skua 2\",\"connection\":\"NotConnected\",\"disconnected\":true,\"hello\":null,\"status\":null,\"log\":[]}]}",
            json);
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("instances")[0].GetProperty("log");
        Assert.Equal(100, first.GetArrayLength());
        Assert.Equal("Sold 99", first[99].GetProperty("text").GetString());
        Assert.Empty(_skua.Sent);
    }

    [Fact]
    public void No_skua_gives_an_empty_instance_list()
    {
        _skua.Current = SkuaHubView.Empty;

        Assert.Equal("{\"instances\":[]}", Text(Get("/api/skua")));
    }

    [Fact]
    public void Without_a_skua_api_the_routes_are_404_as_before()
    {
        var handler = new PhoneHandler(_registry, _hub, new FakeAssets(), h => h == GoodHost, "9.9.9");

        Assert.Equal(404, handler.Handle(Request("GET", "/api/skua", "", null, true, false)).Status);
        Assert.Equal(405, handler.Handle(Request("POST", "/api/skua/a1/stop", "", null, true, true)).Status);
    }

    // ---- scripts ------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_script_list_carries_the_ask_preference_and_the_truncation()
    {
        const string other = "fedcba9876543210";
        _prefs.Set(other, false);
        _skua.AnswerWith(Ok(scripts: [new SkuaScriptInfo(Sid, "Farm", "Farming"), new SkuaScriptInfo(other, "Butler", "")], truncated: true));

        var response = Get("/api/skua/a1/scripts");

        Assert.Equal(200, response.Status);
        Assert.Equal(
            $"{{\"scripts\":[{{\"id\":\"{Sid}\",\"name\":\"Farm\",\"folder\":\"Farming\",\"askOptions\":true,\"title\":null,\"description\":null,\"tags\":null}},"
            + $"{{\"id\":\"{other}\",\"name\":\"Butler\",\"folder\":\"\",\"askOptions\":false,\"title\":null,\"description\":null,\"tags\":null}}],\"truncated\":true}}",
            Text(response));
        var sent = Assert.Single(_skua.Sent);
        Assert.Equal("a1", sent.InstanceId);
        Assert.IsType<ListScriptsCommand>(sent.Command);
    }

    [Fact]
    public void The_script_list_carries_title_description_and_tags()
    {
        _skua.AnswerWith(Ok(scripts: [new SkuaScriptInfo(Sid, "0RunAll", "Hollowborn", "Run all", "does \"all\" of it", "a, b")]));

        using var doc = JsonDocument.Parse(Text(Get("/api/skua/a1/scripts")));

        var item = doc.RootElement.GetProperty("scripts")[0];
        Assert.Equal("0RunAll", item.GetProperty("name").GetString());
        Assert.Equal("Run all", item.GetProperty("title").GetString());
        Assert.Equal("does \"all\" of it", item.GetProperty("description").GetString());
        Assert.Equal("a, b", item.GetProperty("tags").GetString());
    }

    [Fact]
    public void A_list_of_2000_described_scripts_is_answered_whole_with_no_response_size_cap()
    {
        var scripts = Enumerable.Range(1, SkuaProtocol.MaxScripts)
            .Select(i => new SkuaScriptInfo($"{i:x16}", $"Some fairly long script name {i}", "Other/Some folder",
                new string('T', 60), new string('D', 150), new string('G', 80))).ToList();
        _skua.AnswerWith(Ok(scripts: scripts));

        var response = Get("/api/skua/a1/scripts");

        Assert.Equal(200, response.Status);
        Assert.InRange(response.Body.Length, 500_000, 1_000_000);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal(SkuaProtocol.MaxScripts, doc.RootElement.GetProperty("scripts").GetArrayLength());
        Assert.Equal(new string('D', 150), doc.RootElement.GetProperty("scripts")[1999].GetProperty("description").GetString());
    }

    [Fact]
    public void An_answer_without_scripts_is_an_empty_list()
    {
        Assert.Equal("{\"scripts\":[],\"truncated\":false}", Text(Get("/api/skua/a1/scripts")));
    }

    // ---- options ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Options_from_a_plugin_that_knows_the_definitions_are_returned_and_cached()
    {
        _skua.AnswerWith(Ok(defs: [Rate, Mode], values: [new("Options", "Rate", "2.5"), new("Options", "Mode", "Slow")], hasOptions: true));

        var response = Get($"/api/skua/a1/scripts/{Sid}/options");

        Assert.Equal(200, response.Status);
        Assert.Equal(
            "{\"defs\":[{\"category\":\"Options\",\"name\":\"Rate\",\"displayName\":\"Drop rate\",\"description\":\"How fast\",\"type\":\"double\",\"default\":\"1.5\",\"choices\":null},"
            + "{\"category\":\"Options\",\"name\":\"Mode\",\"displayName\":\"Mode\",\"description\":\"\",\"type\":\"enum\",\"default\":\"Fast Farm\",\"choices\":[\"Fast Farm\",\"Slow\"]}],"
            + "\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"2.5\"},{\"category\":\"Options\",\"name\":\"Mode\",\"value\":\"Slow\"}],"
            + "\"known\":true,\"hasOptions\":true,\"discovery\":false,\"askOptions\":true}",
            Text(response));
        Assert.Equal([Rate, Mode], _cache.Get(Sid));
        var sent = Assert.IsType<GetOptionsCommand>(Assert.Single(_skua.Sent).Command);
        Assert.Equal(Sid, sent.ScriptId);
    }

    [Fact]
    public void Raw_file_values_are_normalised_against_the_cached_definitions()
    {
        _cache.Set(Sid, Defs);
        _prefs.Set(Sid, false);
        _skua.AnswerWith(Ok(values: [new("Options", "Rate", "1,5"), new("Options", "Mode", "Fast_Farm"), new("Options", "Follow", "Hero_1"), new("Extra", "Loop", "True")], hasOptions: true));

        using var doc = JsonDocument.Parse(Text(Get($"/api/skua/a1/scripts/{Sid}/options")));
        var root = doc.RootElement;

        Assert.True(root.GetProperty("known").GetBoolean());
        Assert.False(root.GetProperty("discovery").GetBoolean());
        Assert.False(root.GetProperty("askOptions").GetBoolean());
        Assert.Equal(4, root.GetProperty("defs").GetArrayLength());
        Assert.Equal(["1.5", "Fast Farm", "Hero_1", "True"], root.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("value").GetString()));
    }

    [Fact]
    public void Raw_values_without_cached_definitions_stay_as_they_are_and_are_not_known()
    {
        _skua.AnswerWith(Ok(values: [new("Options", "Rate", "1,5")], hasOptions: true));

        Assert.Equal(
            "{\"defs\":null,\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"1,5\"}],\"known\":false,\"hasOptions\":true,\"discovery\":false,\"askOptions\":true}",
            Text(Get($"/api/skua/a1/scripts/{Sid}/options")));
    }

    [Fact]
    public void Nothing_known_means_the_next_start_is_a_discovery()
    {
        Assert.Equal(
            "{\"defs\":null,\"values\":[],\"known\":false,\"hasOptions\":false,\"discovery\":true,\"askOptions\":true}",
            Text(Get($"/api/skua/a1/scripts/{Sid}/options")));
    }

    [Fact]
    public void A_script_without_options_has_an_empty_definition_list()
    {
        _skua.AnswerWith(Ok(defs: [], values: []));

        Assert.Equal(
            "{\"defs\":[],\"values\":[],\"known\":true,\"hasOptions\":false,\"discovery\":false,\"askOptions\":true}",
            Text(Get($"/api/skua/a1/scripts/{Sid}/options")));
        Assert.Empty(_cache.Get(Sid)!);
    }

    [Fact]
    public void Setting_options_on_a_plugin_with_definitions_sends_invariant_numbers_and_enum_members_as_shown()
    {
        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(c switch
        {
            SetOptionsCommand set => Ok(defs: Defs, values: set.Values, hasOptions: true) with { ReqId = c.ReqId },
            _ => Ok(defs: Defs, values: [], hasOptions: true) with { ReqId = c.ReqId },
        });

        var response = Post(
            $"/api/skua/a1/scripts/{Sid}/options",
            "{\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"1.5\"},{\"category\":\"Options\",\"name\":\"Mode\",\"value\":\"Fast_Farm\"},"
            + "{\"category\":\"Extra\",\"name\":\"Loop\",\"value\":\"true\"},{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"Hero\",\"extra\":1}]}");

        Assert.Equal(200, response.Status);
        Assert.Equal(2, _skua.Sent.Count);
        Assert.IsType<GetOptionsCommand>(_skua.Sent[0].Command);
        var set = Assert.IsType<SetOptionsCommand>(_skua.Sent[1].Command);
        Assert.Equal(Sid, set.ScriptId);
        Assert.Equal(
            [new SkuaOptionValue("Options", "Rate", "1.5"), new("Options", "Mode", "Fast Farm"), new("Extra", "Loop", "True"), new("Options", "Follow", "Hero")],
            set.Values);
        using var doc = JsonDocument.Parse(Text(response));
        Assert.True(doc.RootElement.GetProperty("known").GetBoolean());
        Assert.Equal("1.5", doc.RootElement.GetProperty("values")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Setting_options_on_a_plugin_without_definitions_sends_doubles_in_the_pc_culture_and_reads_them_back_invariant()
    {
        _cache.Set(Sid, Defs);
        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(c switch
        {
            SetOptionsCommand set => Ok(values: set.Values, hasOptions: true) with { ReqId = c.ReqId },
            _ => Ok(values: [new("Options", "Rate", "2,5"), new("Options", "Mode", "Fast Farm")], hasOptions: true) with { ReqId = c.ReqId },
        });

        var response = Post($"/api/skua/a1/scripts/{Sid}/options", "{\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"1.5\"},{\"category\":\"Options\",\"name\":\"Mode\",\"value\":\"Slow\"}]}");

        Assert.Equal(200, response.Status);
        var set = Assert.IsType<SetOptionsCommand>(_skua.Sent[1].Command);
        Assert.Equal([new SkuaOptionValue("Options", "Rate", "1,5"), new("Options", "Mode", "Slow")], set.Values);
        using var doc = JsonDocument.Parse(Text(response));
        Assert.Equal("1.5", doc.RootElement.GetProperty("values")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Without_plugin_definitions_only_the_names_in_the_options_file_are_sent()
    {
        // After a Skua restart the plugin accepts only names already in the file; a Transient option (Skua never saves those)
        // or one added to the script since is in the cached definitions but not in the file, and is left out.
        _cache.Set(Sid, Defs);
        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(c switch
        {
            SetOptionsCommand set => Ok(values: set.Values, hasOptions: true) with { ReqId = c.ReqId },
            _ => Ok(values: [new("Options", "Rate", "2,5"), new("Options", "Mode", "Slow"), new("Extra", "Loop", "False")], hasOptions: true) with { ReqId = c.ReqId },
        });

        var response = Post(
            $"/api/skua/a1/scripts/{Sid}/options",
            "{\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"1.5\"},{\"category\":\"Options\",\"name\":\"Mode\",\"value\":\"Fast Farm\"},"
            + "{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"Hero\"},{\"category\":\"Extra\",\"name\":\"Loop\",\"value\":\"True\"}]}");

        Assert.Equal(200, response.Status);
        var set = Assert.IsType<SetOptionsCommand>(_skua.Sent[1].Command);
        Assert.Equal([new SkuaOptionValue("Options", "Rate", "1,5"), new("Options", "Mode", "Fast Farm"), new("Extra", "Loop", "True")], set.Values);
    }

    [Fact]
    public void Without_plugin_definitions_the_cached_definitions_still_check_every_value_sent()
    {
        _cache.Set(Sid, Defs);
        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(Ok(values: [new("Options", "Rate", "2,5")], hasOptions: true) with { ReqId = c.ReqId });

        var response = Post(
            $"/api/skua/a1/scripts/{Sid}/options",
            "{\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"1.5\"},{\"category\":\"Options\",\"name\":\"Mode\",\"value\":\"Medium\"}]}");

        Assert.Equal(400, response.Status);
        Assert.Equal("not a valid enum for Mode", ErrorOf(response));
        Assert.DoesNotContain(_skua.Sent, s => s.Command is SetOptionsCommand);
    }

    [Fact]
    public void With_plugin_definitions_every_value_is_sent_even_one_missing_from_the_file()
    {
        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(c switch
        {
            SetOptionsCommand set => Ok(defs: Defs, values: set.Values, hasOptions: true) with { ReqId = c.ReqId },
            _ => Ok(defs: Defs, values: [new("Options", "Rate", "2.5")], hasOptions: true) with { ReqId = c.ReqId },
        });

        var response = Post(
            $"/api/skua/a1/scripts/{Sid}/options",
            "{\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"1.5\"},{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"Hero\"}]}");

        Assert.Equal(200, response.Status);
        Assert.Equal(
            [new SkuaOptionValue("Options", "Rate", "1.5"), new("Options", "Follow", "Hero")],
            Assert.IsType<SetOptionsCommand>(_skua.Sent[1].Command).Values);
    }

    [Fact]
    public void Without_any_definitions_the_values_go_as_they_are_and_the_plugin_checks_the_names()
    {
        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(c switch
        {
            SetOptionsCommand => new ResultMessage(c.ReqId, false, "unknown option Nope", null, null, null, false, false),
            _ => Ok(values: [new("Options", "Rate", "2,5")], hasOptions: true) with { ReqId = c.ReqId },
        });

        var response = Post($"/api/skua/a1/scripts/{Sid}/options", "{\"values\":[{\"category\":\"Options\",\"name\":\"Nope\",\"value\":\"1.5\"}]}");

        Assert.Equal(400, response.Status);
        Assert.Equal("unknown option Nope", ErrorOf(response));
        Assert.Equal([new SkuaOptionValue("Options", "Nope", "1.5")], Assert.IsType<SetOptionsCommand>(_skua.Sent[1].Command).Values);
    }

    [Theory]
    [InlineData("Options", "Nope", "1", "unknown option Nope")]
    [InlineData("Other", "Rate", "1", "unknown option Other:Rate")]
    [InlineData("Options", "Rate", "1,5", "not a valid double for Rate")]
    [InlineData("Options", "Rate", "fast", "not a valid double for Rate")]
    [InlineData("Options", "Mode", "Medium", "not a valid enum for Mode")]
    [InlineData("Extra", "Loop", "yes", "not a valid bool for Extra:Loop")]
    public void Values_that_do_not_fit_the_cached_definitions_are_400_and_never_written(string category, string name, string value, string error)
    {
        _cache.Set(Sid, Defs);

        var response = Post($"/api/skua/a1/scripts/{Sid}/options", $"{{\"values\":[{{\"category\":\"{category}\",\"name\":\"{name}\",\"value\":\"{value}\"}}]}}");

        Assert.Equal(400, response.Status);
        Assert.Equal(error, ErrorOf(response));
        Assert.DoesNotContain(_skua.Sent, s => s.Command is SetOptionsCommand);
    }

    [Fact]
    public void Values_over_200_characters_line_breaks_and_repeats_are_refused_before_anything_is_sent()
    {
        var path = $"/api/skua/a1/scripts/{Sid}/options";

        var tooLong = Post(path, $"{{\"values\":[{{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"{new string('a', 201)}\"}}]}}");
        Assert.Equal(400, tooLong.Status);
        Assert.Equal("value too long for Follow", ErrorOf(tooLong));
        Assert.Equal(200, Post(path, $"{{\"values\":[{{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"{new string('a', 200)}\"}}]}}").Status);
        _skua.Sent.Clear();

        Assert.Equal("line break in Follow", ErrorOf(Post(path, "{\"values\":[{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"a\\nb\"}]}")));
        Assert.Equal(
            "option Follow given twice",
            ErrorOf(Post(path, "{\"values\":[{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"a\"},{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"b\"}]}")));
        Assert.Empty(_skua.Sent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"values\":{}}")]
    [InlineData("{\"values\":[1]}")]
    [InlineData("{\"values\":[{\"category\":\"Options\",\"name\":\"Rate\"}]}")]
    [InlineData("{\"values\":[{\"category\":\"Options\",\"name\":\"\",\"value\":\"1\"}]}")]
    [InlineData("{\"values\":[{\"category\":\"\",\"name\":\"Rate\",\"value\":\"1\"}]}")]
    [InlineData("{\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":1.5}]}")]
    public void A_malformed_options_body_is_400(string body)
    {
        var response = Post($"/api/skua/a1/scripts/{Sid}/options", body);

        Assert.Equal(400, response.Status);
        Assert.Equal("invalid body", ErrorOf(response));
        Assert.Empty(_skua.Sent);
    }

    [Fact]
    public void A_refused_set_options_passes_the_plugin_text_through()
    {
        _skua.Answer = (_, c) => Task.FromResult<ResultMessage?>(c switch
        {
            SetOptionsCommand => new ResultMessage(c.ReqId, false, "already running", null, null, null, false, false),
            _ => Ok(defs: Defs, values: []) with { ReqId = c.ReqId },
        });

        var response = Post($"/api/skua/a1/scripts/{Sid}/options", "{\"values\":[{\"category\":\"Options\",\"name\":\"Follow\",\"value\":\"Hero\"}]}");

        Assert.Equal(400, response.Status);
        Assert.Equal("already running", ErrorOf(response));
    }

    // ---- start, stop, prefs ---------------------------------------------------------------------------------------------

    [Fact]
    public void Start_sends_the_id_to_the_instance_and_caches_the_definitions_it_learnt()
    {
        _skua.AnswerWith(Ok(defs: [Follow], hasOptions: true));

        var response = Post("/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}");

        Assert.Equal(200, response.Status);
        Assert.Equal(
            "{\"hasOptions\":true,\"firstRun\":false,\"defs\":[{\"category\":\"Options\",\"name\":\"Follow\",\"displayName\":\"Player to follow\",\"description\":\"\",\"type\":\"string\",\"default\":\"\",\"choices\":null}],\"values\":[]}",
            Text(response));
        var sent = Assert.Single(_skua.Sent);
        Assert.Equal("a1", sent.InstanceId);
        Assert.Equal(Sid, Assert.IsType<StartCommand>(sent.Command).ScriptId);
        Assert.Equal([Follow], _cache.Get(Sid));
    }

    [Fact]
    public void A_discovery_start_returns_first_run_with_the_definitions_and_saved_values()
    {
        _skua.AnswerWith(Ok(defs: [Rate], values: [new("Options", "Rate", "1.5")], hasOptions: true, firstRun: true));

        Assert.Equal(
            "{\"hasOptions\":true,\"firstRun\":true,\"defs\":[{\"category\":\"Options\",\"name\":\"Rate\",\"displayName\":\"Drop rate\",\"description\":\"How fast\",\"type\":\"double\",\"default\":\"1.5\",\"choices\":null}],"
            + "\"values\":[{\"category\":\"Options\",\"name\":\"Rate\",\"value\":\"1.5\"}]}",
            Text(Post("/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}")));
    }

    [Fact]
    public void A_start_without_reported_definitions_answers_the_cached_ones()
    {
        _cache.Set(Sid, [Follow]);
        _skua.AnswerWith(Ok(hasOptions: true));

        using var doc = JsonDocument.Parse(Text(Post("/api/skua/a1/start", $"{{\"id\":\"{Sid}\"}}")));

        Assert.Equal(1, doc.RootElement.GetProperty("defs").GetArrayLength());
    }

    [Fact]
    public void Stop_goes_to_the_chosen_instance_only()
    {
        _skua.Current = new SkuaHubView([FakeSkua.Instance("a1", "Hero"), FakeSkua.Instance("c3", "Zed")]);

        Assert.Equal("{\"ok\":true}", Text(Post("/api/skua/c3/stop", "{}")));
        Assert.Equal("c3", Assert.Single(_skua.Sent).InstanceId);
    }

    [Fact]
    public void The_prefs_route_stores_the_choice_and_the_script_list_shows_it()
    {
        var response = Post($"/api/skua/a1/scripts/{Sid}/prefs", "{\"askOptions\":false}");

        Assert.Equal(200, response.Status);
        Assert.Equal($"{{\"id\":\"{Sid}\",\"askOptions\":false}}", Text(response));
        Assert.False(_prefs.AskOptions(Sid));
        Assert.Empty(_skua.Sent);

        Assert.Equal($"{{\"id\":\"{Sid}\",\"askOptions\":true}}", Text(Post($"/api/skua/a1/scripts/{Sid}/prefs", "{\"askOptions\":true}")));
        Assert.True(_prefs.AskOptions(Sid));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"askOptions\":\"false\"}")]
    [InlineData("{\"askOptions\":0}")]
    [InlineData("{\"askOptions\":null}")]
    public void A_prefs_body_without_a_boolean_is_400(string body)
    {
        Assert.Equal("invalid body", ErrorOf(Post($"/api/skua/a1/scripts/{Sid}/prefs", body)));
        Assert.Null(_prefsFile.Text);
    }

    // ---- the state stream ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_state_keeps_its_skua_field_and_adds_every_instance()
    {
        var skua = new FakeSkua
        {
            Current = new SkuaHubView([
                FakeSkua.Instance("b2", "Zed", disconnected: true),
                FakeSkua.Instance("a1", "Hero", status: new SkuaStatus(true, "Hero", null, null, null, false, null, Control: "awaitingOptions")),
            ]),
        };
        using var hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), _clock.GetUtcNow, skua: skua);

        var json = hub.CurrentJson;

        Assert.Contains("\"skua\":{\"connection\":\"Connected\",\"hello\":null,\"status\":{\"loggedIn\":true,\"character\":\"Hero\"", json);
        Assert.Contains(
            "\"skuaInstances\":[{\"instanceId\":\"b2\",\"label\":\"Zed\",\"connection\":\"NotConnected\",\"disconnected\":true,\"hello\":null,\"status\":null,\"log\":[]},"
            + "{\"instanceId\":\"a1\",\"label\":\"Hero\",\"connection\":\"Connected\",\"disconnected\":false,",
            json);
        Assert.Contains("\"control\":\"awaitingOptions\"", json);
    }

    [Fact]
    public void A_state_without_a_skua_feed_has_no_instances()
    {
        Assert.Contains("\"skua\":null,\"skuaInstances\":null}", _hub.CurrentJson);
    }

    [Fact]
    public async Task A_change_of_the_skua_feed_publishes_a_state_with_the_instances()
    {
        var skua = new FakeSkua();
        using var hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), _clock.GetUtcNow, skua: skua);
        using var subscription = hub.Subscribe();

        skua.Current = new SkuaHubView([FakeSkua.Instance("a1", "Hero")]);
        skua.Raise();

        var message = await subscription.Reader.ReadAsync();
        Assert.Equal("state", message.Event);
        Assert.Contains("\"skuaInstances\":[{\"instanceId\":\"a1\"", message.Json);
    }
}
