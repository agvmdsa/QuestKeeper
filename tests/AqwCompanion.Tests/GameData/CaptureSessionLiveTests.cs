using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.GameData;

public class CaptureSessionLiveTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static byte[] GoldExp(int gold, int exp) =>
        PlainJson(Envelope("addGoldExp", new Dictionary<string, object?> { ["intGold"] = gold, ["intExp"] = exp, ["typ"] = "m", ["id"] = 4 }));

    /// <summary>One login followed by one segment holding the given live messages.</summary>
    private static List<TcpSegment> LoginThen(int clientPort = ClientPort, string name = "Fictional Hero", uint isn = 1000, params byte[][] events)
    {
        var traffic = Connection(clientPort: clientPort, name: name, isn: isn);
        var segments = traffic.Server;
        if (events.Length > 0)
        {
            segments.Add(ServerData(traffic.NextServerSequence, events.SelectMany(e => e.Append((byte)0)).ToArray(), clientPort));
        }

        return segments;
    }

    private static CaptureRig NewRig(IEnumerable<TcpSegment> segments, bool gate = true)
    {
        var rig = new CaptureRig { Source = CaptureRig.Feeding(segments) };
        if (gate)
        {
            rig.Delay.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return rig;
    }

    /// <summary>A rig whose source feeds the segments, signals, then listens until it is cancelled.</summary>
    private static (CaptureRig Rig, TaskCompletionSource Reached) Holding(IEnumerable<TcpSegment> segments)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = NewRig([]);
        rig.Source = new FakePacketSource(async (onSegment, token) =>
        {
            foreach (var segment in segments)
            {
                onSegment(segment);
            }

            reached.SetResult();
            try
            {
                await new TaskCompletionSource().Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
        });
        return (rig, reached);
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        using var cts = new CancellationTokenSource(Wait);
        while (!condition())
        {
            Assert.False(cts.IsCancellationRequested, $"Timed out waiting for: {what}.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task A_live_update_replaces_the_snapshot_marks_it_live_and_raises_changed()
    {
        var rig = NewRig(LoginThen(events: [GoldExp(40, 15), GoldExp(10, 5)]));
        var changes = 0;
        rig.Session.Changed += (_, _) => Interlocked.Increment(ref changes);

        await rig.Session.StartAsync().WaitAsync(Wait);

        var snapshot = Assert.Single(rig.Session.Characters);
        Assert.Equal(9876543210L + 50, snapshot.Profile.Gold);
        Assert.Equal(123456L + 20, snapshot.Profile.Exp);
        Assert.True(rig.Session.IsLive("Fictional Hero"));
        Assert.True(rig.Session.IsLive("fictional hero"));
        Assert.Equal(2, rig.Session.LiveUpdateCount("Fictional Hero"));
        Assert.NotNull(rig.Session.LastLiveUpdateUtc("Fictional Hero"));
        Assert.Equal((10L, 5L), (rig.Session.Live.LastGoldGain, rig.Session.Live.LastExpGain));
        Assert.True(changes >= 4);
    }

    [Fact]
    public async Task A_character_without_updates_is_not_live_and_a_new_login_resets_the_live_state()
    {
        var firstRun = NewRig(LoginThen(events: [GoldExp(1, 1)]));
        await firstRun.Session.StartAsync().WaitAsync(Wait);
        Assert.True(firstRun.Session.IsLive("Fictional Hero"));
        Assert.False(firstRun.Session.IsLive("Someone Else"));
        Assert.Equal(0, firstRun.Session.LiveUpdateCount("Someone Else"));
        Assert.Null(firstRun.Session.LastLiveUpdateUtc("Someone Else"));

        var rig = NewRig(LoginThen(events: []));
        await rig.Session.StartAsync().WaitAsync(Wait);

        Assert.False(rig.Session.IsLive("Fictional Hero"));
        Assert.Equal(9876543210L, Assert.Single(rig.Session.Characters).Profile.Gold);
    }

    [Fact]
    public async Task Two_characters_are_updated_independently()
    {
        var first = LoginThen(50001, "First Hero", 1000, GoldExp(10, 1));
        var second = LoginThen(50002, "Second Hero", 90000, GoldExp(1000, 1), GoldExp(1000, 1));
        var rig = NewRig([.. first, .. second]);

        await rig.Session.StartAsync().WaitAsync(Wait);

        var byName = rig.Session.Characters.ToDictionary(c => c.Profile.Name, c => c.Profile.Gold);
        Assert.Equal(9876543220L, byName["First Hero"]);
        Assert.Equal(9876545210L, byName["Second Hero"]);
        Assert.Equal((1, 2), (rig.Session.LiveUpdateCount("First Hero"), rig.Session.LiveUpdateCount("Second Hero")));
    }

    [Fact]
    public async Task A_burst_of_updates_is_saved_once_after_the_delay()
    {
        var events = Enumerable.Range(0, 20).Select(_ => GoldExp(1, 1)).ToArray();
        var (rig, reached) = Holding(LoginThen(events: events));
        var run = rig.Session.StartAsync();
        await reached.Task.WaitAsync(Wait);
        await Eventually(() => rig.Session.LiveUpdateCount("Fictional Hero") == 20, "the burst to be applied");

        Assert.Single(rig.Store.Saved);
        Assert.Contains(CaptureSession.SaveDelay, rig.Delay.Delays);
        Assert.Equal(TimeSpan.FromSeconds(5), CaptureSession.SaveDelay);

        rig.Delay.Gate!.SetResult();
        await Eventually(() => rig.Store.Saved.Count >= 2, "the debounced save");
        await Task.Delay(100);

        Assert.Equal(2, rig.Store.Saved.Count);
        Assert.Equal(9876543210L + 20, Assert.Single(rig.Store.Saved[^1]).Profile.Gold);
        rig.Session.Stop();
        await run.WaitAsync(Wait);
        Assert.Equal(2, rig.Store.Saved.Count);
    }

    [Fact]
    public async Task Stopping_saves_the_pending_updates_at_once_and_the_late_delay_saves_nothing_more()
    {
        var (rig, reached) = Holding(LoginThen(events: [GoldExp(7, 7)]));
        var run = rig.Session.StartAsync();
        await reached.Task.WaitAsync(Wait);
        await Eventually(() => rig.Session.LiveUpdateCount("Fictional Hero") == 1, "the update");
        Assert.Single(rig.Store.Saved);

        rig.Session.Stop();

        Assert.Equal(2, rig.Store.Saved.Count);
        Assert.Equal(9876543217L, Assert.Single(rig.Store.Saved[^1]).Profile.Gold);
        await run.WaitAsync(Wait);
        rig.Delay.Gate!.SetResult();
        await Task.Delay(100);
        Assert.Equal(2, rig.Store.Saved.Count);
    }

    [Fact]
    public async Task A_session_that_ends_by_itself_saves_the_pending_updates()
    {
        var rig = NewRig(LoginThen(events: [GoldExp(7, 7)]));

        await rig.Session.StartAsync().WaitAsync(Wait);

        Assert.Equal(2, rig.Store.Saved.Count);
        Assert.Equal(9876543217L, Assert.Single(rig.Store.Saved[^1]).Profile.Gold);
    }

    [Fact]
    public async Task Quiescing_saves_the_pending_updates_at_once()
    {
        var (rig, reached) = Holding(LoginThen(events: [GoldExp(7, 7)]));
        var run = rig.Session.StartAsync();
        await reached.Task.WaitAsync(Wait);
        await Eventually(() => rig.Session.LiveUpdateCount("Fictional Hero") == 1, "the update");

        ((IForgettable)rig.Session).Quiesce();

        Assert.Equal(2, rig.Store.Saved.Count);
        Assert.Equal(9876543217L, Assert.Single(rig.Store.Saved[^1]).Profile.Gold);
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task After_forgetting_nothing_is_saved_and_the_live_state_is_gone()
    {
        var (rig, reached) = Holding(LoginThen(events: [GoldExp(7, 7)]));
        var run = rig.Session.StartAsync();
        await reached.Task.WaitAsync(Wait);
        await Eventually(() => rig.Session.LiveUpdateCount("Fictional Hero") == 1, "the update");
        var savesBefore = rig.Store.Saved.Count;

        ((IForgettable)rig.Session).Forget();
        rig.Delay.Gate!.SetResult();
        await run.WaitAsync(Wait);
        await Task.Delay(100);

        Assert.Equal(savesBefore, rig.Store.Saved.Count);
        Assert.Empty(rig.Session.Characters);
        Assert.False(rig.Session.IsLive("Fictional Hero"));
        Assert.Equal(0, rig.Session.LiveUpdateCount("Fictional Hero"));
        Assert.Null(rig.Session.LastLiveUpdateUtc("Fictional Hero"));
    }

    [Fact]
    public void The_live_numbers_are_zero_before_the_first_start()
    {
        var rig = new CaptureRig();

        Assert.Equal(CaptureLiveDiagnostics.Empty, rig.Session.Live);
        Assert.False(rig.Session.IsLive("Anyone"));
    }
}
