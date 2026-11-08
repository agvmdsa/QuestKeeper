using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.GameData;

public class CaptureSessionTests
{
    private sealed class Rig
    {
        public FakeProcessElevation Elevation { get; } = new();

        public FakeGameDataStore Store { get; } = new();

        public FakeDelay Delay { get; } = new() { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };

        public FakeApplicationExit Exit { get; } = new();

        public AppPaths Paths { get; } = new("/tmp/aqw-fictional-root");

        public bool IsWindows { get; set; } = true;

        public NpcapStatus Npcap { get; set; } = NpcapStatus.Available;

        public FakePacketSource Source { get; set; } = new((_, _) => Task.CompletedTask);

        public int FactoryCalls { get; private set; }

        public List<string> Events { get; } = [];

        public CaptureSession Create()
        {
            var session = new CaptureSession(
                () =>
                {
                    FactoryCalls++;
                    return Source;
                },
                Elevation, () => Npcap, () => IsWindows, Store, Delay, TimeProvider.System, Exit, Paths);
            session.Changed += (_, _) =>
            {
                lock (Events)
                {
                    Events.Add($"{session.State}:{session.Characters.Count}");
                }
            };
            return session;
        }
    }

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>A source that signals when it runs, feeds the segments, then waits for the token and ends normally.</summary>
    private static (FakePacketSource Source, TaskCompletionSource Started, Func<CancellationToken> Token) Holding(
        IEnumerable<TcpSegment>? segments = null)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken seen = default;
        var source = new FakePacketSource(async (onSegment, token) =>
        {
            seen = token;
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
        return (source, started, () => seen);
    }

    private static FakePacketSource Feeding(IEnumerable<TcpSegment> segments) =>
        new((onSegment, _) =>
        {
            foreach (var segment in segments)
            {
                onSegment(segment);
            }

            return Task.CompletedTask;
        });

    private static IEnumerable<TcpSegment> Interleave(IReadOnlyList<TcpSegment> a, IReadOnlyList<TcpSegment> b) =>
        a.Zip(b).SelectMany(pair => new[] { pair.First, pair.Second });

    [Fact]
    public async Task A_system_that_is_not_windows_is_not_supported_and_the_source_is_untouched()
    {
        var rig = new Rig { IsWindows = false };
        var session = rig.Create();

        await session.StartAsync();

        Assert.Equal(CaptureState.NotSupported, session.State);
        Assert.Equal(0, rig.FactoryCalls);
        Assert.Equal(0, rig.Source.Runs);
    }

    [Fact]
    public async Task Npcap_reporting_not_supported_is_not_supported()
    {
        var rig = new Rig { Npcap = NpcapStatus.NotSupported };
        var session = rig.Create();

        await session.StartAsync();

        Assert.Equal(CaptureState.NotSupported, session.State);
        Assert.Equal(0, rig.FactoryCalls);
    }

    [Fact]
    public async Task Missing_npcap_leaves_the_source_untouched()
    {
        var rig = new Rig { Npcap = NpcapStatus.Missing };
        var session = rig.Create();

        await session.StartAsync();

        Assert.Equal(CaptureState.NpcapMissing, session.State);
        Assert.Equal(0, rig.FactoryCalls);
    }

    [Fact]
    public async Task Missing_administrator_rights_leave_the_source_untouched()
    {
        var rig = new Rig();
        rig.Elevation.IsElevated = false;
        var session = rig.Create();

        await session.StartAsync();

        Assert.Equal(CaptureState.NeedsAdministrator, session.State);
        Assert.Equal(0, rig.FactoryCalls);
        Assert.Equal(["NeedsAdministrator:0"], rig.Events);
    }

    [Fact]
    public async Task A_full_login_is_captured_saved_and_the_session_ends_when_the_source_ends()
    {
        var rig = new Rig { Source = Feeding(Connection().All) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(CaptureState.Done, session.State);
        Assert.Equal(CaptureFailure.None, session.Failure);
        var character = Assert.Single(session.Characters);
        Assert.Equal("Fictional Hero", character.Profile.Name);
        Assert.Equal(1, session.Counters.CharactersCaptured);
        Assert.Equal("Fictional Hero", Assert.Single(Assert.Single(rig.Store.Saved)).Profile.Name);
    }

    [Fact]
    public async Task Changed_is_raised_for_state_changes_and_captures_only()
    {
        var rig = new Rig { Source = Feeding(Connection().All) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(["Listening:0", "Listening:1", "Done:1"], rig.Events);
    }

    [Fact]
    public async Task Two_interleaved_logins_give_two_characters()
    {
        var a = Connection(clientPort: 50000, name: "First Hero");
        var b = Connection(clientPort: 50001, name: "Second Hero");
        var rig = new Rig { Source = Feeding(Interleave(a.Server, b.Server)) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(["First Hero", "Second Hero"], session.Characters.Select(c => c.Profile.Name).Order());
        Assert.Equal(2, session.Counters.CharactersCaptured);
    }

    [Fact]
    public async Task The_same_name_captured_twice_keeps_one_character_ignoring_case()
    {
        var first = Connection(clientPort: 50000, name: "Fictional Hero");
        var second = Connection(clientPort: 50001, name: "FICTIONAL HERO");
        var rig = new Rig { Source = Feeding([.. first.Server, .. second.Server]) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal("FICTIONAL HERO", Assert.Single(session.Characters).Profile.Name);
        Assert.Equal(2, rig.Store.Saved.Count);
        Assert.Single(rig.Store.Saved[1]);
    }

    [Fact]
    public async Task Only_sixteen_characters_are_kept_and_the_oldest_is_dropped()
    {
        var segments = Enumerable.Range(0, CaptureSession.MaxCharacters + 1)
            .SelectMany(i => Connection(clientPort: 50000 + i, name: "Hero " + i).Server);
        var rig = new Rig { Source = Feeding(segments) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(CaptureSession.MaxCharacters, session.Characters.Count);
        Assert.DoesNotContain(session.Characters, c => c.Profile.Name == "Hero 0");
        Assert.Contains(session.Characters, c => c.Profile.Name == "Hero 16");
    }

    [Fact]
    public async Task Stop_ends_a_listening_session_as_done()
    {
        var (source, started, token) = Holding();
        var rig = new Rig { Source = source };
        var session = rig.Create();

        var run = session.StartAsync();
        await started.Task.WaitAsync(Wait);
        Assert.Equal(CaptureState.Listening, session.State);
        session.Stop();

        Assert.Equal(CaptureState.Done, session.State);
        await run.WaitAsync(Wait);
        Assert.Equal(CaptureState.Done, session.State);
        Assert.True(token().IsCancellationRequested);
    }

    [Fact]
    public async Task The_time_limit_cancels_the_source_and_ends_the_session_as_done()
    {
        var (source, started, token) = Holding(Connection().Server);
        var rig = new Rig { Source = source };
        var session = rig.Create();

        var run = session.StartAsync();
        await started.Task.WaitAsync(Wait);
        Assert.Equal(CaptureState.Listening, session.State);
        rig.Delay.Gate!.SetResult();
        await run.WaitAsync(Wait);

        Assert.Equal(CaptureState.Done, session.State);
        Assert.Equal(CaptureFailure.None, session.Failure);
        Assert.True(token().IsCancellationRequested);
        Assert.Equal([CaptureSession.TimeLimit], rig.Delay.Delays);
        Assert.Equal(TimeSpan.FromMinutes(10), CaptureSession.TimeLimit);
        Assert.Single(session.Characters);
    }

    [Fact]
    public async Task A_source_that_ends_at_once_without_traffic_is_a_failure_without_a_device()
    {
        var rig = new Rig();
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(CaptureState.Failed, session.State);
        Assert.Equal(CaptureFailure.NoDevice, session.Failure);
    }

    [Fact]
    public async Task A_source_that_ends_after_traffic_without_a_character_is_done()
    {
        var rig = new Rig { Source = Feeding(Connection(includeInventory: false).Server) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(CaptureState.Done, session.State);
        Assert.Empty(session.Characters);
        Assert.True(session.Counters.SegmentsSeen > 0);
    }

    [Fact]
    public async Task A_source_that_throws_is_a_driver_error()
    {
        var rig = new Rig { Source = new FakePacketSource((_, _) => throw new InvalidOperationException("driver")) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(CaptureState.Failed, session.State);
        Assert.Equal(CaptureFailure.DriverError, session.Failure);
    }

    [Fact]
    public async Task A_source_factory_that_throws_is_a_driver_error()
    {
        var session = new CaptureSession(
            () => throw new InvalidOperationException("driver"), new FakeProcessElevation(), () => NpcapStatus.Available,
            () => true, new FakeGameDataStore(), new FakeDelay { Gate = new TaskCompletionSource() }, TimeProvider.System,
            new FakeApplicationExit(), new AppPaths("/tmp/x"));

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal((CaptureState.Failed, CaptureFailure.DriverError), (session.State, session.Failure));
    }

    [Fact]
    public async Task A_second_start_while_listening_is_a_no_op()
    {
        var (source, started, _) = Holding();
        var rig = new Rig { Source = source };
        var session = rig.Create();
        var run = session.StartAsync();
        await started.Task.WaitAsync(Wait);

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(1, rig.FactoryCalls);
        Assert.Equal(1, source.Runs);
        session.Stop();
        await run.WaitAsync(Wait);
    }

    [Fact]
    public void Relaunching_passes_the_data_root_and_exits_only_when_it_worked()
    {
        var rig = new Rig();
        var session = rig.Create();

        Assert.False(session.RelaunchAsAdministrator());
        Assert.Equal(0, rig.Exit.ExitCalls);

        rig.Elevation.RelaunchResult = true;
        Assert.True(session.RelaunchAsAdministrator());
        Assert.Equal(1, rig.Exit.ExitCalls);
        Assert.Equal([rig.Paths.Root, rig.Paths.Root], rig.Elevation.RelaunchedWith);
    }

    private static CharacterSnapshot Stored(string name) => new(
        new CharacterProfile(name, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0),
        [new FactionStanding(1, "Fictional Order", 1, 0, 900, 0)], [], [], DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Restore_loads_the_stored_characters_once()
    {
        var rig = new Rig();
        rig.Store.Stored.Add(Stored("Stored Hero"));
        var session = rig.Create();

        await session.RestoreAsync();
        await session.RestoreAsync();

        Assert.Equal("Stored Hero", Assert.Single(session.Characters).Profile.Name);
        Assert.Equal(1, rig.Store.LoadCalls);
        Assert.Equal(["Idle:1"], rig.Events);
    }

    [Fact]
    public async Task Restore_does_not_overwrite_characters_that_are_already_there()
    {
        var rig = new Rig { Source = Feeding(Connection().Server) };
        rig.Store.Stored.Add(Stored("Stored Hero"));
        var session = rig.Create();
        await session.StartAsync().WaitAsync(Wait);

        await session.RestoreAsync();

        Assert.Equal("Fictional Hero", Assert.Single(session.Characters).Profile.Name);
    }

    [Fact]
    public async Task Restore_is_ignored_after_forgetting()
    {
        var rig = new Rig();
        rig.Store.Stored.Add(Stored("Stored Hero"));
        var session = rig.Create();
        IForgettable forgettable = session;
        forgettable.Quiesce();
        forgettable.Forget();

        await session.RestoreAsync();

        Assert.Empty(session.Characters);
    }

    [Fact]
    public void The_session_is_forgettable_under_the_name_game_data() =>
        Assert.Equal("Game data", ((IForgettable)new Rig().Create()).Name);

    [Fact]
    public async Task Quiesce_stops_a_running_session_and_refuses_new_starts_until_resume()
    {
        var (source, started, token) = Holding();
        var rig = new Rig { Source = source };
        var session = rig.Create();
        IForgettable forgettable = session;
        var run = session.StartAsync();
        await started.Task.WaitAsync(Wait);

        forgettable.Quiesce();

        Assert.Equal(CaptureState.Done, session.State);
        await run.WaitAsync(Wait);
        Assert.True(token().IsCancellationRequested);

        await session.StartAsync().WaitAsync(Wait);
        Assert.Equal(1, rig.FactoryCalls);

        forgettable.Resume();
        rig.Source = Feeding(Connection().Server);
        await session.StartAsync().WaitAsync(Wait);
        Assert.Equal(2, rig.FactoryCalls);
        Assert.Single(session.Characters);
    }

    [Fact]
    public async Task Details_are_empty_before_a_run_follow_the_source_and_are_cleared_by_Forget()
    {
        var rig = new Rig { Source = Feeding(Connection().Server) };
        rig.Source.Text = "Packets from the driver: 5";
        var session = rig.Create();
        Assert.Equal(string.Empty, session.Details);

        await session.StartAsync().WaitAsync(Wait);
        Assert.Equal("Packets from the driver: 5", session.Details);

        rig.Source.Text = "Packets from the driver: 6";
        Assert.Equal("Packets from the driver: 6", session.Details);

        ((IForgettable)session).Forget();
        Assert.Equal(string.Empty, session.Details);
        Assert.Equal(default, session.Stages);
    }

    [Fact]
    public async Task Stages_count_the_parts_of_a_login()
    {
        var rig = new Rig { Source = Feeding(Connection().Server) };
        var session = rig.Create();

        await session.StartAsync().WaitAsync(Wait);

        Assert.Equal(new CaptureStageCounters(0, 1, 1, 0), session.Stages);
    }

    [Fact]
    public async Task Forget_clears_everything_and_allows_starting_again()
    {
        var rig = new Rig { Source = Feeding(Connection().Server) };
        var session = rig.Create();
        IForgettable forgettable = session;
        await session.StartAsync().WaitAsync(Wait);
        Assert.Single(session.Characters);

        forgettable.Quiesce();
        forgettable.Forget();

        Assert.Empty(session.Characters);
        Assert.Equal(default, session.Counters);
        Assert.Equal((CaptureState.Idle, CaptureFailure.None), (session.State, session.Failure));

        await session.StartAsync().WaitAsync(Wait);
        Assert.Equal(2, rig.FactoryCalls);
    }

    [Fact]
    public async Task A_capture_that_completes_after_forgetting_is_ignored()
    {
        var release = new TaskCompletionSource();
        var login = Connection().Server;
        var source = new FakePacketSource(async (onSegment, _) =>
        {
            await release.Task;
            foreach (var segment in login)
            {
                onSegment(segment);
            }
        });
        var rig = new Rig { Source = source };
        var session = rig.Create();
        IForgettable forgettable = session;
        var run = session.StartAsync();

        forgettable.Quiesce();
        forgettable.Forget();
        release.SetResult();
        await run.WaitAsync(Wait);

        Assert.Empty(session.Characters);
        Assert.Empty(rig.Store.Saved);
        Assert.Equal(CaptureState.Idle, session.State);
    }
}
