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
    public void Awaiting_options_alone_raises_nothing_however_long_it_lasts()
    {
        // The plugin may stay in awaitingOptions up to its own options wait (5 s) with no dialog at all; only it knows
        // when the dialog is really open, and it says so with optionsStuck.
        var engine = Started();
        Assert.Empty(engine.ProcessSkua(View("awaitingOptions"), T0.AddSeconds(1)));

        Assert.Empty(engine.Tick(T0.AddSeconds(4.5)));
        Assert.Empty(engine.ProcessSkua(View("awaitingOptions"), T0.AddSeconds(6)));
        Assert.Empty(engine.Tick(T0.AddSeconds(30)));
    }

    [Fact]
    public void Options_stuck_raises_the_notice_with_its_words()
    {
        var engine = Started();
        engine.ProcessSkua(View("awaitingOptions"), T0.AddSeconds(1));

        var notice = Assert.Single(engine.ProcessSkua(View("awaitingOptions", stuck: true), T0.AddSeconds(11)));

        Assert.Equal(NoticeKind.SkuaOptionsWaiting, notice.Kind);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Waiting for you on the PC", notice.Title);
        Assert.Equal("Hero: close the script's options window on the computer.", notice.Body);
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
        SkuaHubView Stuck(bool a, bool b) => new([.. View("awaitingOptions", stuck: a).Instances, .. View("awaitingOptions", stuck: b, id: "b", label: "Zed").Instances]);
        engine.ProcessSkua(Both("awaitingOptions", "idle"), T0);

        Assert.Equal("Hero", Assert.Single(engine.ProcessSkua(Stuck(true, false), T0.AddSeconds(10))).Character);
        Assert.Equal("Zed", Assert.Single(engine.ProcessSkua(Stuck(true, true), T0.AddSeconds(12))).Character);
    }

    [Fact]
    public void Reset_forgets_the_waits()
    {
        var engine = Started();
        engine.ProcessSkua(View("awaitingOptions", stuck: true), T0);

        engine.Reset();

        // After a reset the next view is a baseline again: a wait in it is not announced.
        Assert.Empty(engine.ProcessSkua(View("awaitingOptions", stuck: true), T0.AddSeconds(10)));
        Assert.Empty(engine.Tick(T0.AddSeconds(20)));
    }

    [Fact]
    public void The_hub_publishes_the_notice_when_a_status_says_options_stuck()
    {
        var clock = new ManualClock(T0);
        var skua = new FakeSkua { Current = View("idle") };
        using var hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), clock.GetUtcNow, skua: skua);
        using var subscription = hub.Subscribe();

        skua.Current = View("awaitingOptions");
        skua.Raise();
        clock.Advance(TimeSpan.FromSeconds(6));
        hub.Tick();
        Assert.DoesNotContain(hub.Current.Notices, n => n.Kind == NoticeKind.SkuaOptionsWaiting);

        skua.Current = View("awaitingOptions", stuck: true);
        skua.Raise();

        Assert.Contains(hub.Current.Notices, n => n.Kind == NoticeKind.SkuaOptionsWaiting);
        var messages = new List<HubMessage>();
        while (subscription.Reader.TryRead(out var message))
        {
            messages.Add(message);
        }

        Assert.Contains(messages, m => m.Event == "notice" && m.Json.Contains("\"kind\":\"SkuaOptionsWaiting\"", StringComparison.Ordinal));
    }
}
