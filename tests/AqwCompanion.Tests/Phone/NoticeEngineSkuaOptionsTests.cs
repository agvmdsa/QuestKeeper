using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class NoticeEngineSkuaOptionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static SkuaHubView View(string? control, bool stuck = false, bool disconnected = false, string id = "a", string label = "Hero") =>
        new([FakeSkua.Instance(id, label, disconnected, new SkuaStatus(true, label, null, null, "Farm.cs", true, null, Control: control, OptionsStuck: stuck))]);

    private static NoticeEngine Started()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View("idle"), T0);
        return engine;
    }

    [Fact]
    public void Awaiting_options_for_more_than_three_seconds_raises_one_notice_on_a_tick()
    {
        var engine = Started();
        Assert.Empty(engine.ProcessSkua(View("awaitingOptions"), T0.AddSeconds(1)));

        Assert.Empty(engine.Tick(T0.AddSeconds(4)));   // exactly 3 s: not more
        Assert.Equal(TimeSpan.Zero, engine.SkuaOptionsDueIn(T0.AddSeconds(4)));
        var notice = Assert.Single(engine.Tick(T0.AddSeconds(4.5)));
        Assert.Empty(engine.Tick(T0.AddSeconds(30)));

        Assert.Equal(NoticeKind.SkuaOptionsWaiting, notice.Kind);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Waiting for you on the PC", notice.Title);
        Assert.Equal("Hero: close the script's options window on the computer.", notice.Body);
        Assert.Null(engine.SkuaOptionsDueIn(T0.AddSeconds(30)));
    }

    [Fact]
    public void A_short_wait_raises_nothing()
    {
        var engine = Started();
        engine.ProcessSkua(View("awaitingOptions"), T0.AddSeconds(1));

        Assert.Empty(engine.ProcessSkua(View("running"), T0.AddSeconds(3)));
        Assert.Empty(engine.Tick(T0.AddSeconds(10)));
        Assert.Null(engine.SkuaOptionsDueIn(T0.AddSeconds(10)));
    }

    [Fact]
    public void Options_stuck_raises_at_once_and_only_once_per_wait()
    {
        var engine = Started();
        engine.ProcessSkua(View("awaitingOptions"), T0.AddSeconds(1));

        Assert.Single(engine.ProcessSkua(View("awaitingOptions", stuck: true), T0.AddSeconds(2)));
        Assert.Empty(engine.ProcessSkua(View("awaitingOptions", stuck: true), T0.AddSeconds(20)));
        Assert.Empty(engine.Tick(T0.AddSeconds(30)));

        // A new wait after it ended is a new notice.
        engine.ProcessSkua(View("running"), T0.AddSeconds(40));
        Assert.Single(engine.ProcessSkua(View(null, stuck: true), T0.AddSeconds(50)));
    }

    [Fact]
    public void The_wait_raised_on_a_status_change_after_three_seconds_counts_too()
    {
        var engine = Started();
        engine.ProcessSkua(View("awaitingOptions"), T0);

        Assert.Single(engine.ProcessSkua(View("awaitingOptions", id: "a") with { }, T0.AddSeconds(5)));
    }

    [Fact]
    public void A_wait_already_there_in_the_baseline_is_not_announced()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View("awaitingOptions", stuck: true), T0);

        Assert.Empty(engine.Tick(T0.AddSeconds(10)));
        Assert.Empty(engine.ProcessSkua(View("awaitingOptions", stuck: true), T0.AddSeconds(11)));
    }

    [Fact]
    public void A_disconnected_instance_raises_no_options_notice()
    {
        var engine = Started();

        var notices = engine.ProcessSkua(View("awaitingOptions", stuck: true, disconnected: true), T0.AddSeconds(1));

        Assert.DoesNotContain(notices, n => n.Kind == NoticeKind.SkuaOptionsWaiting);
    }

    [Fact]
    public void Each_instance_waits_on_its_own()
    {
        var engine = Started();
        SkuaHubView Both(string? a, string? b) => new([.. View(a).Instances, .. View(b, id: "b", label: "Zed").Instances]);
        engine.ProcessSkua(Both("awaitingOptions", "idle"), T0);
        engine.ProcessSkua(Both("awaitingOptions", "awaitingOptions"), T0.AddSeconds(2));

        Assert.Equal("Hero", Assert.Single(engine.Tick(T0.AddSeconds(4))).Character);
        Assert.Equal("Zed", Assert.Single(engine.Tick(T0.AddSeconds(6))).Character);
    }

    [Fact]
    public void Reset_forgets_the_waits()
    {
        var engine = Started();
        engine.ProcessSkua(View("awaitingOptions"), T0);

        engine.Reset();

        Assert.Empty(engine.Tick(T0.AddSeconds(10)));
        Assert.Null(engine.SkuaOptionsDueIn(T0.AddSeconds(10)));
    }

    [Fact]
    public void The_hub_asks_for_a_look_when_a_wait_is_due_and_publishes_the_notice()
    {
        var clock = new ManualClock(T0);
        var skua = new FakeSkua { Current = View("idle") };
        var scheduled = new List<(TimeSpan Delay, Action Action)>();
        using var hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), clock.GetUtcNow, skua: skua, schedule: (d, a) => scheduled.Add((d, a)));
        using var subscription = hub.Subscribe();

        skua.Current = View("awaitingOptions");
        skua.Raise();
        skua.Raise();   // one look pending at most

        var (delay, action) = Assert.Single(scheduled);
        Assert.Equal(TimeSpan.FromSeconds(3.1), delay);
        clock.Advance(delay);
        action();

        Assert.Contains(hub.Current.Notices, n => n.Kind == NoticeKind.SkuaOptionsWaiting);
        Assert.Single(scheduled);
        var messages = new List<HubMessage>();
        while (subscription.Reader.TryRead(out var message))
        {
            messages.Add(message);
        }

        Assert.Contains(messages, m => m.Event == "notice" && m.Json.Contains("\"kind\":\"SkuaOptionsWaiting\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_scheduler_the_regular_tick_finds_the_wait()
    {
        var clock = new ManualClock(T0);
        var skua = new FakeSkua { Current = View("idle") };
        using var hub = new PhoneHub(new FakeFeedSource(FeedSnapshot.Empty), new NoticeEngine(), new NoticeLog(50), clock.GetUtcNow, skua: skua);

        skua.Current = View("awaitingOptions");
        skua.Raise();
        clock.Advance(TimeSpan.FromSeconds(15));
        hub.Tick();

        Assert.Contains(hub.Current.Notices, n => n.Kind == NoticeKind.SkuaOptionsWaiting);
    }
}
