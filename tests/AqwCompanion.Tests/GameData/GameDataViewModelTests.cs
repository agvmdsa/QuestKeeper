using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.GameData;

public class GameDataViewModelTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static byte[] Named(string cmd) => PlainJson(Envelope(cmd, new Dictionary<string, object?> { ["id"] = 1 }));

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
        Assert.Equal("It starts by itself when the app opens and listens until you press Stop capture. It reads only the login data shown here, never your e-mail, IP address or password.", GameDataViewModel.PrivacyText);
        Assert.Equal(
            "The capture is already listening. Open the game launcher and log in; your data appears here when the login has been read.",
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
    public async Task Listening_says_how_many_characters_were_captured_so_far()
    {
        var (zeroSource, zeroStarted) = Holding();
        var zeroRig = new CaptureRig { Source = zeroSource };
        var zero = zeroRig.CreateViewModel();
        var zeroRun = zero.StartCommand.ExecuteAsync(null);
        await zeroStarted.Task.WaitAsync(Wait);
        Assert.Equal("Waiting for a login. Open the game launcher now.", zero.StateText);
        zero.StopCommand.Execute(null);
        await zeroRun.WaitAsync(Wait);

        var (oneSource, oneStarted) = Holding(Connection().Server);
        var oneRig = new CaptureRig { Source = oneSource };
        var one = oneRig.CreateViewModel();
        var oneRun = one.StartCommand.ExecuteAsync(null);
        await oneStarted.Task.WaitAsync(Wait);
        Assert.True(one.IsListening);
        Assert.Equal("Listening. 1 character captured so far. Log in another client to add more, or press Stop capture.", one.StateText);
        one.StopCommand.Execute(null);
        await oneRun.WaitAsync(Wait);

        var (twoSource, twoStarted) = Holding(
            Connection(clientPort: 50000, name: "First Hero").Server.Concat(Connection(clientPort: 50001, name: "Second Hero").Server));
        var twoRig = new CaptureRig { Source = twoSource };
        var two = twoRig.CreateViewModel();
        var twoRun = two.StartCommand.ExecuteAsync(null);
        await twoStarted.Task.WaitAsync(Wait);
        Assert.Equal("Listening. 2 characters captured so far. Log in another client to add more, or press Stop capture.", two.StateText);
        two.StopCommand.Execute(null);
        await twoRun.WaitAsync(Wait);
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
            () => rig.Source, rig.Elevation, () => rig.Npcap, () => rig.IsWindows, store, TimeProvider.System, rig.Exit,
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

    [Fact]
    public void The_decoding_and_commands_lines_are_empty_when_everything_is_zero()
    {
        var vm = new CaptureRig().CreateViewModel();

        Assert.Equal(string.Empty, vm.DecodingText);
        Assert.Equal(string.Empty, vm.CommandsText);
        Assert.False(vm.HasDecoding);
        Assert.False(vm.HasCommands);
    }

    [Fact]
    public async Task The_commands_line_lists_the_known_commands_and_the_profile_split()
    {
        // The standard traffic already carries another player's profile and an initUserDatas; one more profile without gold joins them.
        var (_, vm) = await Captured(Connection(includeProfile: false, extraServerMessages: [EncodeBlob(InitUserData(ProfileData(own: false)))]).Server);

        Assert.Equal(
            "Commands read: initUserData 2 (own profile 0, ignored 2: no gold 2, no name 0) · initUserDatas 1 · loadInventoryBig 1 · mtls 1",
            vm.CommandsText);
        Assert.True(vm.HasCommands);
        Assert.Equal(string.Empty, vm.DecodingText);
    }

    [Fact]
    public async Task The_other_commands_line_lists_unknown_names_with_counts_and_is_empty_otherwise()
    {
        Assert.Equal(string.Empty, new CaptureRig().CreateViewModel().OtherNamesText);
        Assert.False(new CaptureRig().CreateViewModel().HasOtherNames);

        var stream = new[] { Named("repGain"), Named("repGain"), Named("levelUp"), Named("two words") }
            .SelectMany(m => m.Append((byte)0)).ToArray();
        var held = Holding([SynAck(1000), ServerData(1001, stream)]);
        var rig = new CaptureRig { Source = held.Source };
        var vm = rig.CreateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _ = vm.StartCommand.ExecuteAsync(null);
        await held.Started.Task.WaitAsync(Wait);
        vm.RefreshCommand.Execute(null);

        Assert.Equal("Other commands seen: repGain 2 · levelUp 1", vm.OtherNamesText);
        Assert.True(vm.HasOtherNames);
        Assert.Contains(nameof(GameDataViewModel.OtherNamesText), raised);
        Assert.DoesNotContain("two words", vm.OtherNamesText);
        vm.StopCommand.Execute(null);
    }

    [Fact]
    public async Task The_decoding_line_lists_only_the_reasons_that_happened_and_refresh_updates_it()
    {
        var garbage = new[] { Bytes("Z" + new string('!', 70)), Bytes("{\"a\":1}"), Bytes("{\"a\":2}") };
        var stream = garbage.SelectMany(m => m.Append((byte)0)).ToArray();
        var held = Holding([SynAck(1000), ServerData(1001, stream)]);
        var rig = new CaptureRig { Source = held.Source };
        var vm = rig.CreateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _ = vm.StartCommand.ExecuteAsync(null);
        await held.Started.Task.WaitAsync(Wait);
        vm.RefreshCommand.Execute(null);

        Assert.Equal("Unreadable: not base64 1 · no command 2", vm.DecodingText);
        Assert.True(vm.HasDecoding);
        Assert.Contains(nameof(GameDataViewModel.DecodingText), raised);
        Assert.Contains(nameof(GameDataViewModel.CommandsText), raised);
        vm.StopCommand.Execute(null);
    }

    [Fact]
    public void The_connections_and_samples_lines_are_empty_before_anything_is_seen()
    {
        var vm = new CaptureRig().CreateViewModel();

        Assert.Equal(string.Empty, vm.ConnectionsText);
        Assert.Equal(string.Empty, vm.SamplesText);
        Assert.False(vm.HasConnections);
        Assert.False(vm.HasSamples);
    }

    [Fact]
    public async Task The_connections_line_counts_the_login()
    {
        var (_, vm) = await Captured(Connection().Server);

        Assert.Equal(
            "Connections: 1 seen, 1 with the start of the stream, 1 with a profile, 1 with an inventory, 1 completed",
            vm.ConnectionsText);
        Assert.True(vm.HasConnections);
        Assert.Equal(string.Empty, vm.SamplesText);
    }

    [Fact]
    public async Task The_samples_line_describes_unreadable_messages_by_class_and_refresh_updates_it()
    {
        var gzip = Bytes("Z" + Convert.ToBase64String([0x1F, 0x8B, .. Enumerable.Repeat((byte)0xFF, 60)]));
        var notBase64 = Bytes("Z" + new string('!', 70));
        var stream = new[] { gzip, notBase64 }.SelectMany(m => m.Append((byte)0)).ToArray();
        var held = Holding([SynAck(1000), ServerData(1001, stream)]);
        var rig = new CaptureRig { Source = held.Source };
        var vm = rig.CreateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _ = vm.StartCommand.ExecuteAsync(null);
        await held.Started.Task.WaitAsync(Wait);
        vm.RefreshCommand.Execute(null);

        Assert.Matches(
            @"^Unreadable messages: #1 85 B \(drop-first-1\) -> 62 B gzip header, entropy 0\.\d · #2 71 B \(none\) -> 0 B empty, entropy 0\.0$",
            vm.SamplesText);
        Assert.Equal("Connections: 1 seen, 1 with the start of the stream, 0 with a profile, 0 with an inventory, 0 completed", vm.ConnectionsText);
        Assert.True(vm.HasSamples);
        Assert.Contains(nameof(GameDataViewModel.SamplesText), raised);
        Assert.Contains(nameof(GameDataViewModel.ConnectionsText), raised);
        vm.StopCommand.Execute(null);
    }

    private static CharacterSnapshot Stored(string name) => new(
        new CharacterProfile(name, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0),
        [], [], [], DateTimeOffset.UnixEpoch);

    private static readonly string StoredTime = LocalTimeText.Format(DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task A_restored_character_is_labelled_as_saved()
    {
        var rig = new CaptureRig();
        rig.Store.Stored.Add(Stored("Stored Hero"));
        var vm = rig.CreateViewModel();

        await rig.Session.RestoreAsync();

        Assert.Equal($"Saved from an earlier session ({StoredTime}). Log in again to refresh it.", vm.CapturedText);
        Assert.True(vm.HasCapturedText);
    }

    [Fact]
    public async Task A_capture_of_the_same_name_in_this_run_is_labelled_as_this_session()
    {
        var rig = new CaptureRig { Source = Feeding(Connection(name: "stored hero").Server) };
        rig.Store.Stored.Add(Stored("Stored Hero"));
        var vm = rig.CreateViewModel();
        await rig.Session.RestoreAsync();

        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        var captured = Assert.Single(rig.Session.Characters).CapturedAtUtc;
        Assert.Equal($"Captured {LocalTimeText.Format(captured)} in this session.", vm.CapturedText);
    }

    [Fact]
    public async Task A_capture_of_another_name_leaves_the_restored_one_saved()
    {
        var rig = new CaptureRig { Source = Feeding(Connection().Server) };
        rig.Store.Stored.Add(Stored("Stored Hero"));
        var vm = rig.CreateViewModel();
        await rig.Session.RestoreAsync();
        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);

        vm.SelectedCharacter = "Fictional Hero";
        Assert.StartsWith("Captured ", vm.CapturedText);
        vm.SelectedCharacter = "Stored Hero";

        Assert.Equal($"Saved from an earlier session ({StoredTime}). Log in again to refresh it.", vm.CapturedText);
    }

    [Fact]
    public async Task Forgetting_empties_the_captured_text()
    {
        var rig = new CaptureRig { Source = Feeding(Connection().Server) };
        var vm = rig.CreateViewModel();
        await vm.StartCommand.ExecuteAsync(null).WaitAsync(Wait);
        Assert.True(vm.HasCapturedText);

        ((IForgettable)rig.Session).Forget();

        Assert.Equal(string.Empty, vm.CapturedText);
        Assert.False(vm.HasCapturedText);
    }

    private static IEnumerable<TcpSegment> WithoutSynAck(int clientPort = 50001) =>
        [ServerData(5000, [.. Named("repGain"), 0], clientPort)];

    [Fact]
    public void The_hint_text_is_the_planned_one()
    {
        Assert.Equal(
            "A game session that started before this app was opened is running. Its data cannot be read until you log in again, for example by switching server.",
            GameDataViewModel.EarlierSessionHintText);
    }

    [Fact]
    public async Task A_stream_without_a_start_that_carries_server_messages_shows_the_hint()
    {
        var (_, vm) = await Captured(WithoutSynAck());

        Assert.True(vm.HasHint);
        Assert.Equal(GameDataViewModel.EarlierSessionHintText, vm.HintText);
    }

    [Fact]
    public async Task A_login_on_another_connection_hides_the_hint()
    {
        var (_, vm) = await Captured(WithoutSynAck(), Connection().Server);

        Assert.Equal(string.Empty, vm.HintText);
        Assert.False(vm.HasHint);
    }

    [Fact]
    public async Task A_normal_login_only_never_shows_the_hint()
    {
        var (_, vm) = await Captured(Connection().Server);

        Assert.False(vm.HasHint);
    }

    [Fact]
    public void Before_any_traffic_there_is_no_hint()
    {
        Assert.False(new CaptureRig().CreateViewModel().HasHint);
    }
}
