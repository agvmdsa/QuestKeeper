using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneHubTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = T0;
    }

    private static FeedCharacter Hero(string name, DateTimeOffset? last = null) =>
        new(name, 60, true, T0, last, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null);

    private static FeedSnapshot Base(string? selected = "Alpha", int rank = 8) =>
        new(1, new FeedCapture("Listening.", true), selected, [Hero("Alpha", T0)], [new FeedFaction("Yokai", rank, 0, 0, 0, false)], null);

    private static (PhoneHub Hub, FakeFeedSource Source, Clock Clock) NewHub(FeedSnapshot? initial = null)
    {
        var clock = new Clock();
        var source = new FakeFeedSource(initial ?? Base());
        var hub = new PhoneHub(source, new NoticeEngine(), new NoticeLog(50), () => clock.Now);
        return (hub, source, clock);
    }

    private static List<HubMessage> Drain(HubSubscription s)
    {
        var l = new List<HubMessage>();
        while (s.Reader.TryRead(out var m))
        {
            l.Add(m);
        }

        return l;
    }

    [Fact]
    public void A_new_hub_exposes_the_source_snapshot_and_no_notices()
    {
        var (hub, source, _) = NewHub();

        Assert.Equal(source.Current, hub.Current.Snapshot);
        Assert.Empty(hub.Current.Notices);
    }

    [Fact]
    public void A_changed_snapshot_publishes_one_state_message_with_the_new_selected_name()
    {
        var (hub, source, _) = NewHub();
        using var sub = hub.Subscribe();

        source.Current = Base(selected: "Bravo");
        source.Raise();

        var messages = Drain(sub);
        var message = Assert.Single(messages);
        Assert.Equal("state", message.Event);
        Assert.Contains("\"selected\":\"Bravo\"", message.Json);
    }

    [Fact]
    public void An_equal_snapshot_publishes_nothing()
    {
        var (hub, source, _) = NewHub();
        using var sub = hub.Subscribe();

        source.Current = Base();
        source.Raise();

        Assert.Empty(Drain(sub));
    }

    [Fact]
    public void A_rank_up_publishes_a_notice_then_a_state_and_is_kept_in_the_notices()
    {
        var (hub, source, clock) = NewHub();
        using var sub = hub.Subscribe();

        clock.Now = T0.AddSeconds(5);
        source.Current = Base(rank: 9);
        source.Raise();

        var messages = Drain(sub);
        Assert.Equal(["notice", "state"], messages.Select(m => m.Event));
        var notice = Assert.Single(hub.Current.Notices);
        Assert.Equal(NoticeKind.RankUp, notice.Kind);
    }

    [Fact]
    public void Tick_after_eleven_minutes_of_silence_publishes_one_stall_notice_and_no_state()
    {
        var (hub, _, clock) = NewHub();
        using var sub = hub.Subscribe();

        clock.Now = T0.AddMinutes(11);
        hub.Tick();
        var first = Drain(sub);
        hub.Tick();
        var second = Drain(sub);

        var message = Assert.Single(first);
        Assert.Equal("notice", message.Event);
        Assert.Contains("ActivityStopped", message.Json);
        Assert.Empty(second);
    }

    [Fact]
    public void Two_subscribers_receive_the_same_json_instance()
    {
        var (hub, source, _) = NewHub();
        using var a = hub.Subscribe();
        using var b = hub.Subscribe();

        source.Current = Base(selected: "Bravo");
        source.Raise();

        Assert.Same(Assert.Single(Drain(a)).Json, Assert.Single(Drain(b)).Json);
    }

    [Fact]
    public async Task Disposing_a_subscription_lowers_the_count_and_completes_its_reader()
    {
        var (hub, _, _) = NewHub();
        var sub = hub.Subscribe();
        Assert.Equal(1, hub.SubscriberCount);

        sub.Dispose();

        Assert.Equal(0, hub.SubscriberCount);
        await sub.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_hundred_changes_without_a_reader_keep_the_newest_sixty_four_and_never_block()
    {
        var (hub, source, _) = NewHub();
        using var sub = hub.Subscribe();

        for (var i = 0; i < 100; i++)
        {
            source.Current = Base(selected: $"Name{i}");
            source.Raise();
        }

        var messages = Drain(sub);
        Assert.Equal(64, messages.Count);
        Assert.Contains("\"selected\":\"Name99\"", messages[^1].Json);
        Assert.Contains("\"selected\":\"Name36\"", messages[0].Json);
    }

    [Fact]
    public void Reset_empties_the_notices_resets_the_engine_and_publishes_one_state()
    {
        var (hub, source, clock) = NewHub();
        clock.Now = T0.AddSeconds(5);
        source.Current = Base(rank: 9);
        source.Raise();
        Assert.Single(hub.Current.Notices);
        using var sub = hub.Subscribe();

        hub.Reset();

        Assert.Empty(hub.Current.Notices);
        var message = Assert.Single(Drain(sub));
        Assert.Equal("state", message.Event);

        // The engine forgot the rank-up id: going down and up again announces it anew.
        source.Current = Base(rank: 8);
        source.Raise();
        Assert.Empty(hub.Current.Notices);
        source.Current = Base(rank: 9);
        source.Raise();
        Assert.Single(hub.Current.Notices);
    }

    [Fact]
    public void Disposing_the_hub_unsubscribes_from_the_source()
    {
        var (hub, source, _) = NewHub();
        var sub = hub.Subscribe();
        Assert.Equal(1, source.Subscribers);

        hub.Dispose();
        source.Current = Base(selected: "Bravo");
        source.Raise();

        Assert.Equal(0, source.Subscribers);
        Assert.Empty(Drain(sub));
    }

    [Fact]
    public void Serialize_uses_camel_case_string_enums_and_keeps_nulls()
    {
        var notice = new Notice("n1", NoticeKind.RankUp, "Alpha", "Rank up", "Yokai reached rank 9.", T0);
        var state = new PhoneState(Base(selected: null), [notice]);

        var json = PhoneJson.Serialize(state);

        Assert.Contains("\"selected\":null", json);
        Assert.Contains("\"kind\":\"RankUp\"", json);
        Assert.Contains("\"snapshot\":", json);
    }
}
