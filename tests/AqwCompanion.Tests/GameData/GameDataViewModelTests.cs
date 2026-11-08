using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.GameData;

public class GameDataViewModelTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static FakePacketSource Feeding(IEnumerable<TcpSegment> segments) =>
        new((onSegment, _) =>
        {
            foreach (var segment in segments)
            {
                onSegment(segment);
            }

            return Task.CompletedTask;
        });

    /// <summary>Feeds the segments, signals, then waits for the token.</summary>
    private static (FakePacketSource Source, TaskCompletionSource Started) Holding(IEnumerable<TcpSegment>? segments = null)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakePacketSource(async (onSegment, token) =>
        {
            foreach (var segment in segments ?? [])
            {
                onSegment(segment);
            }

            started.SetResult();
            try
            {
                await new TaskCompletionSource().Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
        });
        return (source, started);
    }

    private static async Task<(CaptureRig Rig, GameDataViewModel Vm)> Captured(params IEnumerable<TcpSegment>[] connections)
    {
        var rig = new CaptureRig { Source = Feeding(connections.SelectMany(c => c)) };
        var vm = rig.CreateViewModel();
        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);
        return (rig, vm);
    }

    private static IEnumerable<TcpSegment> WithProfile(Dictionary<string, object?> profile, int clientPort = 50000) =>
        Connection(clientPort: clientPort, includeProfile: false, extraServerMessages: [EncodeBlob(InitUserData(profile))]).Server;

    [Fact]
    public void The_texts_are_the_planned_ones()
    {
        Assert.Equal("Game data", GameDataViewModel.TitleText);
        Assert.Equal("Start capture", GameDataViewModel.StartText);
        Assert.Equal("Stop capture", GameDataViewModel.StopText);
        Assert.Equal("Restart as administrator", GameDataViewModel.RestartText);
        Assert.Equal("https://npcap.com/", GameDataViewModel.NpcapUrl);
        Assert.Equal("The app reads only the login data shown here. It never keeps your e-mail address, IP address or password.", GameDataViewModel.PrivacyText);
        Assert.Equal(
            "Press Start capture, then open the game launcher and log in. Your data appears here when the login has been read.",
            GameDataViewModel.EmptyText);
    }

    [Fact]
    public void A_new_view_model_is_idle_and_empty()
    {
        var vm = new CaptureRig().CreateViewModel();

        Assert.Equal("Not capturing.", vm.StateText);
        Assert.Equal("Segments seen 0 · dropped 0 · messages read 0 · characters captured 0 · unreadable 0 · profile parts 0 · inventory parts 0", vm.CountersText);
        Assert.False(vm.IsListening);
        Assert.True(vm.CanStart);
        Assert.False(vm.HasCharacters);
        Assert.False(vm.HasSeveralCharacters);
        Assert.Empty(vm.CharacterNames);
        Assert.Null(vm.SelectedCharacter);
        Assert.Null(vm.ActiveSnapshot);
        Assert.Equal(string.Empty, vm.ProfileHeadline);
        Assert.Empty(vm.ProfileLines);
        Assert.Empty(vm.Factions);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task A_system_that_is_not_windows_says_so()
    {
        var rig = new CaptureRig { IsWindows = false };
        var vm = rig.CreateViewModel();

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal("Capturing is only available on Windows.", vm.StateText);
    }

    [Fact]
    public async Task Missing_npcap_is_explained_and_flagged()
    {
        var rig = new CaptureRig { Npcap = NpcapStatus.Missing };
        var vm = rig.CreateViewModel();

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal("Npcap is not installed. Install it from https://npcap.com/ and press Start capture again.", vm.StateText);
        Assert.True(vm.NpcapMissing);
        Assert.False(vm.NeedsAdministrator);
    }

    [Fact]
    public async Task Missing_administrator_rights_enable_the_restart_command_only_then()
    {
        var rig = new CaptureRig();
        rig.Elevation.IsElevated = false;
        rig.Elevation.RelaunchResult = true;
        var vm = rig.CreateViewModel();
        Assert.False(vm.RestartAsAdministratorCommand.CanExecute(null));

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal("Capturing needs administrator rights.", vm.StateText);
        Assert.True(vm.NeedsAdministrator);
        Assert.True(vm.RestartAsAdministratorCommand.CanExecute(null));
        vm.RestartAsAdministratorCommand.Execute(null);
        Assert.Equal(["/tmp/aqw-fictional-root"], rig.Elevation.RelaunchedWith);
        Assert.Equal(1, rig.Exit.ExitCalls);
    }

    [Fact]
    public async Task Listening_shows_the_waiting_text_and_swaps_start_for_stop()
    {
        var (source, started) = Holding();
        var rig = new CaptureRig { Source = source };
        var vm = rig.CreateViewModel();
        Assert.False(vm.StopCommand.CanExecute(null));

        var run = vm.StartCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(Wait);

        Assert.Equal("Waiting for a login. Open the game launcher now.", vm.StateText);
        Assert.True(vm.IsListening);
        Assert.False(vm.CanStart);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));
        vm.StopCommand.Execute(null);
        await run.WaitAsync(Wait);

        Assert.Equal("Capture finished.", vm.StateText);
        Assert.False(vm.IsListening);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_finished_capture_with_characters_names_how_many()
    {
        var (_, vm) = await Captured(Connection().Server);
        Assert.Equal("Capture finished. 1 character captured.", vm.StateText);

        var (_, two) = await Captured(Connection(clientPort: 50000, name: "First Hero").Server, Connection(clientPort: 50001, name: "Second Hero").Server);
        Assert.Equal("Capture finished. 2 characters captured.", two.StateText);
    }

    [Fact]
    public async Task A_finished_capture_without_characters_says_only_that_it_finished()
    {
        var (_, vm) = await Captured(Connection(includeInventory: false).Server);

        Assert.Equal("Capture finished.", vm.StateText);
    }

    [Fact]
    public async Task Failures_are_explained()
    {
        var noDevice = new CaptureRig();
        var noDeviceVm = noDevice.CreateViewModel();
        await noDeviceVm.StartCommand.ExecuteAsync(null);
        Assert.Equal("No network device could be opened.", noDeviceVm.StateText);

        var driver = new CaptureRig { Source = new FakePacketSource((_, _) => throw new InvalidOperationException("driver")) };
        var driverVm = driver.CreateViewModel();
        await driverVm.StartCommand.ExecuteAsync(null);
        Assert.Equal("The capture driver reported an error.", driverVm.StateText);
    }

    [Fact]
    public async Task The_counters_text_formats_numbers_with_thousands_separators()
    {
        var (rig, vm) = await Captured(Connection(segmentSize: 1).Server);

        var c = rig.Session.Counters;
        var seen = c.SegmentsSeen;
        Assert.True(seen > 999);
        string N(int v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(
            $"Segments seen {N(seen)} · dropped {N(c.SegmentsDropped)} · messages read {N(c.MessagesDecoded)} · characters captured 1 · unreadable 0 · profile parts 1 · inventory parts 1",
            vm.CountersText);
        Assert.Contains(",", vm.CountersText);
    }

    [Fact]
    public async Task Two_characters_show_the_selector_with_sorted_names()
    {
        var (_, vm) = await Captured(
            Connection(clientPort: 50000, name: "beta Hero").Server,
            Connection(clientPort: 50001, name: "Alpha Hero").Server);

        Assert.True(vm.HasCharacters);
        Assert.True(vm.HasSeveralCharacters);
        Assert.Equal(["Alpha Hero", "beta Hero"], vm.CharacterNames);
        Assert.Equal("beta Hero", vm.SelectedCharacter);
        Assert.Equal("beta Hero", vm.ActiveSnapshot!.Profile.Name);
    }

    [Fact]
    public async Task Restored_characters_select_the_most_recently_captured_one()
    {
        var rig = new CaptureRig();
        var store = new FakeGameDataStore();
        store.Stored.Add(Snapshot("Alpha Hero", new DateTimeOffset(2030, 1, 3, 0, 0, 0, TimeSpan.Zero)));
        store.Stored.Add(Snapshot("Zeta Hero", new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        store.Stored.Add(Snapshot("Mid Hero", new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var session = new CaptureSession(
            () => rig.Source, rig.Elevation, () => rig.Npcap, () => rig.IsWindows, store, rig.Delay, TimeProvider.System, rig.Exit,
            new AppPaths("/tmp/aqw-fictional-root"));
        var vm = new GameDataViewModel(session);

        await session.RestoreAsync().WaitAsync(Wait);

        Assert.Equal(["Alpha Hero", "Mid Hero", "Zeta Hero"], vm.CharacterNames);
        Assert.Equal("Alpha Hero", vm.SelectedCharacter);
    }

    [Fact]
    public async Task A_selection_that_disappears_falls_back_to_the_first_name()
    {
        var rig = new CaptureRig { Source = Feeding(Enumerable.Range(0, CaptureSession.MaxCharacters + 1).SelectMany(i => Connection(clientPort: 50000 + i, name: "Hero " + (char)('A' + i)).Server)) };
        var vm = rig.CreateViewModel();

        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        // "Hero A" was selected first and dropped as the oldest when the seventeenth arrived.
        Assert.Equal(CaptureSession.MaxCharacters, vm.CharacterNames.Count);
        Assert.DoesNotContain("Hero A", vm.CharacterNames);
        Assert.Equal(vm.CharacterNames[0], vm.SelectedCharacter);
    }

    private static CharacterSnapshot Snapshot(string name, DateTimeOffset at) =>
        new(new CharacterProfile(name, 60, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, true, 11, 0, 0, 0, 0), [], [], [], at);

    [Fact]
    public async Task One_character_does_not_show_the_selector()
    {
        var (_, vm) = await Captured(Connection().Server);

        Assert.True(vm.HasCharacters);
        Assert.False(vm.HasSeveralCharacters);
        Assert.Equal("Fictional Hero", vm.SelectedCharacter);
    }

    [Fact]
    public async Task Choosing_a_character_changes_the_shown_data_and_the_selection_survives_a_later_capture()
    {
        var (rig, vm) = await Captured(
            Connection(clientPort: 50000, name: "First Hero").Server,
            Connection(clientPort: 50001, name: "Second Hero").Server);
        Assert.Equal("First Hero", vm.SelectedCharacter);
        var changes = 0;
        vm.ActiveSnapshotChanged += (_, _) => changes++;

        vm.SelectedCharacter = "second hero";

        Assert.Equal(1, changes);
        Assert.Equal("Second Hero", vm.SelectedCharacter);
        Assert.Equal("Second Hero · Lv 60", vm.ProfileHeadline);
        rig.Source = Feeding(Connection(clientPort: 50002, name: "Third Hero").Server);
        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        Assert.Equal(["First Hero", "Second Hero", "Third Hero"], vm.CharacterNames);
        Assert.Equal("Second Hero", vm.SelectedCharacter);
        Assert.Equal("Second Hero · Lv 60", vm.ProfileHeadline);
    }

    [Fact]
    public async Task Setting_an_unknown_or_empty_selection_changes_nothing()
    {
        var (_, vm) = await Captured(Connection().Server);

        vm.SelectedCharacter = "Nobody";
        vm.SelectedCharacter = null;

        Assert.Equal("Fictional Hero", vm.SelectedCharacter);
    }

    [Fact]
    public async Task Capturing_the_selected_character_again_raises_the_active_snapshot_change()
    {
        var (rig, vm) = await Captured(Connection().Server);
        var before = vm.ActiveSnapshot;
        var changes = 0;
        vm.ActiveSnapshotChanged += (_, _) => changes++;
        rig.Source = Feeding(Connection(clientPort: 50001).Server);

        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        Assert.Equal("Fictional Hero", vm.SelectedCharacter);
        Assert.NotSame(before, vm.ActiveSnapshot);
        Assert.True(changes >= 1);
    }

    [Fact]
    public async Task The_profile_shows_the_headline_and_the_formatted_lines()
    {
        var (_, vm) = await Captured(Connection().Server);

        Assert.Equal("Fictional Hero · Lv 60", vm.ProfileHeadline);
        Assert.Equal(
            [
                "Gold: 9,876,543,210",
                "AC: 777",
                "Experience: 123,456 / 654,321",
                "Class points: 5,000",
                "Days played: 300",
                "Membership: Member, 25 days left",
                "Bag slots: 90",
                "Bank slots: 80",
                "House slots: 70",
                "Loadouts: 5",
                "XP boost: 10",
                "Gold boost: 20",
                "Class points boost: 30",
                "Reputation boost: 40",
            ],
            vm.ProfileLines);
    }

    [Fact]
    public async Task Boosts_that_are_zero_are_left_out_and_an_expired_membership_is_shown_as_expired()
    {
        var data = ProfileData();
        data["iUpg"] = 0;
        data["iUpgDays"] = -3;
        data["iBoostXP"] = 0;
        data["iBoostG"] = 0;
        data["iBoostCP"] = 0;
        data["iBoostRep"] = 0;
        var (_, vm) = await Captured(WithProfile(data));

        Assert.Contains("Membership: Expired 3 days ago", vm.ProfileLines);
        Assert.DoesNotContain(vm.ProfileLines, line => line.Contains("boost"));
        Assert.Equal("Loadouts: 5", vm.ProfileLines[^1]);
    }

    [Fact]
    public async Task A_character_who_is_not_a_member_says_so()
    {
        var data = ProfileData();
        data["iUpg"] = 0;
        data["iUpgDays"] = 0;
        var (_, vm) = await Captured(WithProfile(data));

        Assert.Contains("Membership: Not a member", vm.ProfileLines);
    }

    [Fact]
    public async Task Factions_are_sorted_by_name_with_rank_progress_and_what_is_missing()
    {
        var (_, vm) = await Captured(Connection().Server);

        Assert.Equal(
            [
                new GameFactionRow("Fictional Order", "Rank 3", "5,000", "1,400 / 6,400", "5,000"),
                new GameFactionRow("Fresh Order", "Rank 1", "0", "0 / 900", "900"),
                new GameFactionRow("Other Order", "Rank 10", "302,500", "Max rank", "0"),
            ],
            vm.Factions);
    }

    [Fact]
    public async Task Items_are_sorted_by_type_then_name_with_their_flags_in_a_fixed_order()
    {
        var inventory = LoadInventoryBig(
            [Faction(5, "Fictional Order", 5000)],
            [
                Item("Zed Blade", "Sword", itemId: 1, qty: 3, level: 40, temp: true, bank: true, quest: true, wear: true, equip: true, upgrade: true, coins: true),
                Item("Alpha Blade", "Sword", itemId: 2),
                Item("Fictional Class", "Class", itemId: 3, classId: 9),
            ],
            [Item("Fictional Chair", "House Item", itemId: 4, house: true)]);
        var connection = Connection(includeInventory: false, extraServerMessages: [EncodeBlob(inventory)]);
        var (_, vm) = await Captured(connection.Server);

        Assert.Equal(
            [
                new GameItemRow("Fictional Class", "Class", "1", "1", string.Empty),
                new GameItemRow("Fictional Chair", "House Item", "1", "1", "House"),
                new GameItemRow("Alpha Blade", "Sword", "1", "1", string.Empty),
                new GameItemRow("Zed Blade", "Sword", "3", "40", "AC · Member · Equipped · Worn · Quest · Bank · Temp"),
            ],
            vm.Items);
        Assert.Equal("4 items", vm.ItemsSummary);
    }

    [Fact]
    public async Task The_item_filter_narrows_by_name_ignoring_case_and_the_summary_says_so()
    {
        var (_, vm) = await Captured(Connection().Server);
        Assert.Equal("3 items", vm.ItemsSummary);

        vm.ItemFilter = "  BLADE ";

        Assert.Equal("Fictional Blade", Assert.Single(vm.Items).Name);
        Assert.Equal("1 of 3 items", vm.ItemsSummary);
        vm.ItemFilter = "nothing like this";
        Assert.Empty(vm.Items);
        Assert.Equal("0 of 3 items", vm.ItemsSummary);
        vm.ItemFilter = string.Empty;
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("3 items", vm.ItemsSummary);
    }

    [Fact]
    public async Task The_session_changes_are_marshalled_through_onUiThread()
    {
        var rig = new CaptureRig { Source = Feeding(Connection().Server) };
        var posted = new List<Action>();
        var vm = rig.CreateViewModel(posted.Add);

        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        Assert.NotEmpty(posted);
        Assert.Equal("Not capturing.", vm.StateText);
        Assert.False(vm.HasCharacters);
        foreach (var action in posted.ToList())
        {
            action();
        }

        Assert.Equal("Capture finished. 1 character captured.", vm.StateText);
        Assert.Equal("Fictional Hero", vm.SelectedCharacter);
    }

    [Fact]
    public async Task Forgetting_empties_everything_and_clears_the_selection()
    {
        var (rig, vm) = await Captured(Connection().Server);
        vm.ItemFilter = "blade";
        var changes = 0;
        vm.ActiveSnapshotChanged += (_, _) => changes++;

        ((IForgettable)rig.Session).Forget();

        Assert.Equal(1, changes);
        Assert.Equal("Not capturing.", vm.StateText);
        Assert.Equal("Segments seen 0 · dropped 0 · messages read 0 · characters captured 0 · unreadable 0 · profile parts 0 · inventory parts 0", vm.CountersText);
        Assert.False(vm.HasCharacters);
        Assert.Empty(vm.CharacterNames);
        Assert.Null(vm.SelectedCharacter);
        Assert.Null(vm.ActiveSnapshot);
        Assert.Equal(string.Empty, vm.ProfileHeadline);
        Assert.Empty(vm.ProfileLines);
        Assert.Empty(vm.Factions);
        Assert.Empty(vm.Items);
        Assert.Equal(string.Empty, vm.ItemFilter);
        Assert.Equal("0 items", vm.ItemsSummary);
    }

    [Fact]
    public void Refresh_text_is_the_planned_one_and_a_new_view_model_has_no_details()
    {
        var vm = new CaptureRig().CreateViewModel();

        Assert.Equal("Refresh numbers", GameDataViewModel.RefreshText);
        Assert.Equal(string.Empty, vm.DetailsText);
        Assert.False(vm.HasDetails);
    }

    [Fact]
    public async Task The_details_follow_the_source_after_a_run_and_the_refresh_command_re_reads_without_a_change_event()
    {
        var (source, started) = Holding();
        source.Text = "Devices: 1 found";
        var rig = new CaptureRig { Source = source };
        var vm = rig.CreateViewModel();
        var run = vm.StartCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(Wait);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        source.Text = "Devices: 2 found";
        Assert.NotEqual("Devices: 2 found", vm.DetailsText);

        vm.RefreshCommand.Execute(null);

        Assert.Equal("Devices: 2 found", vm.DetailsText);
        Assert.True(vm.HasDetails);
        Assert.Contains(nameof(GameDataViewModel.DetailsText), raised);
        Assert.Contains(nameof(GameDataViewModel.CountersText), raised);
        Assert.Contains(nameof(GameDataViewModel.HasDetails), raised);
        vm.StopCommand.Execute(null);
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task The_refresh_command_re_reads_the_counters_of_a_running_capture()
    {
        var segments = Connection().Server;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = new CaptureRig();
        var vm = rig.CreateViewModel(onUiThread: _ => { });
        rig.Source = new FakePacketSource(async (onSegment, token) =>
        {
            foreach (var segment in segments)
            {
                onSegment(segment);
            }

            delivered.SetResult();
            await gate.Task.WaitAsync(token);
        });
        var run = rig.Session.StartAsync();
        await delivered.Task.WaitAsync(Wait);
        Assert.Contains("Segments seen 0 ", vm.CountersText);

        vm.RefreshCommand.Execute(null);

        Assert.StartsWith("Segments seen " + rig.Session.Counters.SegmentsSeen.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), vm.CountersText);
        Assert.Contains("characters captured 1 · unreadable 0 · profile parts 1 · inventory parts 1", vm.CountersText);
        rig.Session.Stop();
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task Property_changes_are_announced_for_the_state_and_the_data()
    {
        var rig = new CaptureRig { Source = Feeding(Connection().Server) };
        var vm = rig.CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        foreach (var name in new[]
                 {
                     nameof(GameDataViewModel.StateText), nameof(GameDataViewModel.CountersText), nameof(GameDataViewModel.IsListening),
                     nameof(GameDataViewModel.CharacterNames), nameof(GameDataViewModel.HasCharacters), nameof(GameDataViewModel.SelectedCharacter),
                     nameof(GameDataViewModel.ProfileHeadline), nameof(GameDataViewModel.ProfileLines), nameof(GameDataViewModel.Factions),
                     nameof(GameDataViewModel.Items), nameof(GameDataViewModel.ItemsSummary),
                 })
        {
            Assert.Contains(name, changed);
        }
    }
}
