using AqwCompanion.App.GameData;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.Core.Capture;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.GameData;

public class GameDataViewModelLiveTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static byte[] GoldExp(int gold, int exp) =>
        PlainJson(Envelope("addGoldExp", new Dictionary<string, object?> { ["intGold"] = gold, ["intExp"] = exp, ["typ"] = "m", ["id"] = 4 }));

    private static List<TcpSegment> LoginThen(params byte[][] events)
    {
        var traffic = Connection();
        var segments = traffic.Server;
        if (events.Length > 0)
        {
            segments.Add(ServerData(traffic.NextServerSequence, events.SelectMany(e => e.Append((byte)0)).ToArray()));
        }

        return segments;
    }

    private static async Task<(CaptureRig Rig, GameDataViewModel Vm)> Run(params byte[][] events)
    {
        var rig = new CaptureRig { Source = CaptureRig.Feeding(LoginThen(events)) };
        var vm = rig.CreateViewModel();
        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);
        return (rig, vm);
    }

    [Fact]
    public async Task The_live_text_is_empty_without_updates()
    {
        var (_, vm) = await Run();

        Assert.Equal(string.Empty, vm.LiveText);
        Assert.False(vm.HasLiveText);
        Assert.Equal(string.Empty, vm.LastGainText);
        Assert.False(vm.HasLastGain);
    }

    [Fact]
    public async Task The_live_text_counts_one_update_in_the_singular()
    {
        var (_, vm) = await Run(GoldExp(40, 15));

        var since = LocalTimeText.Format(vm.ActiveSnapshot!.CapturedAtUtc);
        Assert.Equal(
            $"Live: 1 update since {since}. Buying, quests, reputation and level-ups are not tracked yet; log in again to refresh.",
            vm.LiveText);
        Assert.True(vm.HasLiveText);
    }

    [Fact]
    public async Task The_live_text_counts_several_updates()
    {
        var (_, vm) = await Run(GoldExp(40, 15), GoldExp(1, 1), GoldExp(1, 1));

        var since = LocalTimeText.Format(vm.ActiveSnapshot!.CapturedAtUtc);
        Assert.Equal(
            $"Live: 3 updates since {since}. Buying, quests, reputation and level-ups are not tracked yet; log in again to refresh.",
            vm.LiveText);
    }

    [Fact]
    public async Task The_last_kill_line_shows_the_gains_of_the_last_gold_event()
    {
        var (_, vm) = await Run(GoldExp(5, 5), GoldExp(1200, 340));

        Assert.Equal("Last kill: +1,200 gold, +340 exp", vm.LastGainText);
        Assert.True(vm.HasLastGain);
    }

    [Fact]
    public async Task The_last_kill_line_is_empty_when_only_other_events_arrived()
    {
        var (_, vm) = await Run(PlainJson(Envelope("updateClass", new Dictionary<string, object?> { ["iCP"] = 9 })));

        Assert.True(vm.HasLiveText);
        Assert.Equal(string.Empty, vm.LastGainText);
    }

    [Fact]
    public async Task The_profile_lines_follow_the_live_updates_without_a_refresh()
    {
        var (_, vm) = await Run(GoldExp(40, 15));

        Assert.Contains("Gold: 9,876,543,250", vm.ProfileLines);
        Assert.Contains("Experience: 123,471 / 654,321", vm.ProfileLines);
        Assert.Equal(9876543250L, vm.ActiveSnapshot!.Profile.Gold);
    }

    [Fact]
    public async Task The_capture_numbers_follow_the_updates_without_pressing_refresh()
    {
        var (rig, vm) = await Run(GoldExp(40, 15));

        var seen = rig.Session.Counters.SegmentsSeen;
        Assert.StartsWith($"Segments seen {seen} ", vm.CountersText);
        Assert.True(seen >= 4);
    }

    [Fact]
    public async Task Selling_an_item_adds_the_gold_to_the_profile()
    {
        var sell = PlainJson(Envelope("sellItem", new Dictionary<string, object?>
        {
            ["CharItemID"] = 5512, ["iQty"] = 1, ["iQtyNow"] = 0, ["intAmount"] = 25, ["bCoins"] = 0,
        }));

        var (_, vm) = await Run(sell);

        Assert.Contains("Gold: 9,876,543,235", vm.ProfileLines);
    }

    [Fact]
    public async Task The_active_snapshot_changed_event_fires_for_every_update_so_other_tabs_follow()
    {
        var rig = new CaptureRig { Source = CaptureRig.Feeding(LoginThen(GoldExp(1, 1), GoldExp(1, 1))) };
        var vm = rig.CreateViewModel();
        var fired = 0;
        vm.ActiveSnapshotChanged += (_, _) => fired++;

        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        Assert.True(fired >= 1);
        Assert.Equal(9876543212L, vm.ActiveSnapshot!.Profile.Gold);
    }

    [Fact]
    public async Task A_burst_of_updates_queues_one_refresh_on_the_ui_thread_not_one_per_update()
    {
        var queued = new List<Action>();
        var events = Enumerable.Range(0, 100).Select(_ => GoldExp(1, 1)).ToArray();
        var rig = new CaptureRig { Source = CaptureRig.Feeding(LoginThen(events)) };
        var vm = rig.CreateViewModel(queued.Add);
        var profileChanges = 0;
        var stateChanges = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameDataViewModel.ProfileLines))
            {
                profileChanges++;
            }

            if (e.PropertyName == nameof(GameDataViewModel.StateText))
            {
                stateChanges++;
            }
        };

        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        Assert.InRange(queued.Count, 1, 3);
        foreach (var action in queued.ToList())
        {
            action();
        }

        Assert.InRange(stateChanges, 1, 3);
        Assert.InRange(profileChanges, 1, 3);
        Assert.Contains("Gold: 9,876,543,310", vm.ProfileLines);
        Assert.StartsWith("Live: 100 updates since", vm.LiveText);
    }

    [Fact]
    public async Task After_a_queued_refresh_ran_the_next_change_queues_again()
    {
        var queued = new List<Action>();
        var rig = new CaptureRig { Source = CaptureRig.Feeding(LoginThen(GoldExp(1, 1))) };
        var vm = rig.CreateViewModel(queued.Add);
        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);
        foreach (var action in queued.ToList())
        {
            action();
        }

        var before = queued.Count;
        rig.Source = CaptureRig.Feeding(LoginThen(GoldExp(1, 1)));
        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        Assert.True(queued.Count > before);
    }

    [Fact]
    public void The_capture_details_title_and_the_privacy_text_are_the_planned_ones()
    {
        Assert.Equal("Capture details", GameDataViewModel.CaptureDetailsText);
        Assert.Equal(
            "It starts by itself when the app opens and listens until you press Stop capture. It reads your login data and your own gold, experience, item and class point events, and never your e-mail address, IP address, password or other players' data.",
            GameDataViewModel.PrivacyText);
    }
}
