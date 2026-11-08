using AqwCompanion.App.GameData;
using AqwCompanion.App.Shell;
using AqwCompanion.Capture;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.Shell;

public class TrayControllerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private sealed class RecordingExit(List<string> log) : IApplicationExit
    {
        public int Calls { get; private set; }

        public void Exit()
        {
            Calls++;
            log.Add("exit");
        }
    }

    [Fact]
    public void Toggling_debug_from_the_tray_flips_the_debug_mode()
    {
        var settings = new AqwCompanion.App.Settings.SettingsViewModel(
            new FakeAppSettingsStore(), new AqwCompanion.Core.Forgetting.ForgetCoordinator(() => true, () => []));
        var host = new FakeDebugWindowHost();
        var setup = new Setup();
        var controller = new TrayController(
            setup.Rig.Session, setup.Window, setup.Exit, debug: new AqwCompanion.App.Debug.DebugModeController(settings, host));

        controller.ToggleDebug();
        Assert.True(host.IsOpen);

        controller.ToggleDebug();
        Assert.False(host.IsOpen);
    }

    [Fact]
    public void Toggling_debug_without_a_debug_mode_does_nothing()
    {
        var setup = new Setup();

        var ex = Record.Exception(setup.Controller.ToggleDebug);

        Assert.Null(ex);
    }

    private sealed class Setup
    {
        public Setup(TimeSpan? stopTimeout = null)
        {
            Exit = new RecordingExit(Log);
            Window = new FakeMainWindowHost(Log);
            Rig = new CaptureRig();
            Controller = new TrayController(Rig.Session, Window, Exit, stopTimeout);
        }

        public List<string> Log { get; } = [];

        public RecordingExit Exit { get; }

        public FakeMainWindowHost Window { get; }

        public CaptureRig Rig { get; }

        public TrayController Controller { get; }
    }

    /// <summary>A source that feeds the segments, signals, then waits for the token.</summary>
    private static (FakePacketSource Source, TaskCompletionSource Started) Holding(IEnumerable<AqwCompanion.Core.Capture.TcpSegment>? segments = null)
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

    [Fact]
    public void A_close_by_the_user_hides_the_window_and_is_cancelled()
    {
        var setup = new Setup();

        var cancel = setup.Controller.ShouldCancelClose(userInitiated: true);

        Assert.True(cancel);
        Assert.Equal(1, setup.Window.HideCalls);
    }

    [Fact]
    public void A_close_that_is_not_from_the_user_goes_through_and_does_not_hide()
    {
        var setup = new Setup();

        var cancel = setup.Controller.ShouldCancelClose(userInitiated: false);

        Assert.False(cancel);
        Assert.Equal(0, setup.Window.HideCalls);
    }

    [Fact]
    public async Task After_exit_started_no_close_is_cancelled()
    {
        var setup = new Setup();

        await setup.Controller.ExitAsync();

        Assert.True(setup.Controller.IsExiting);
        Assert.False(setup.Controller.ShouldCancelClose(userInitiated: true));
        Assert.Equal(0, setup.Window.HideCalls);
    }

    [Fact]
    public async Task Exit_stops_a_running_capture_before_it_exits()
    {
        var setup = new Setup();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Rig.Source = new FakePacketSource(async (_, token) =>
        {
            started.SetResult();
            try
            {
                await new TaskCompletionSource().Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
            }

            await Task.Delay(50);
            lock (setup.Log)
            {
                setup.Log.Add("closed");
            }
        });
        var run = setup.Rig.Session.StartAsync();
        await started.Task.WaitAsync(Wait);

        await setup.Controller.ExitAsync().WaitAsync(Wait);

        Assert.Equal(["closed", "exit"], setup.Log);
        Assert.Equal(CaptureState.Done, setup.Rig.Session.State);
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task Exit_waits_at_most_the_timeout_for_a_source_that_ignores_the_cancellation()
    {
        var setup = new Setup(TimeSpan.FromMilliseconds(100));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Rig.Source = new FakePacketSource(async (_, _) =>
        {
            started.SetResult();
            await release.Task;
        });
        try
        {
            var run = setup.Rig.Session.StartAsync();
            await started.Task.WaitAsync(Wait);

            await setup.Controller.ExitAsync().WaitAsync(Wait);

            Assert.Equal(1, setup.Exit.Calls);
            Assert.False(run.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task Exit_is_idempotent()
    {
        var setup = new Setup();

        await setup.Controller.ExitAsync();
        await setup.Controller.ExitAsync();

        Assert.Equal(1, setup.Exit.Calls);
    }

    [Fact]
    public async Task Exit_works_with_an_idle_session()
    {
        var setup = new Setup();

        await setup.Controller.ExitAsync().WaitAsync(Wait);

        Assert.Equal(1, setup.Exit.Calls);
        Assert.Equal(CaptureState.Idle, setup.Rig.Session.State);
    }

    [Fact]
    public void Open_shows_the_window()
    {
        var setup = new Setup();

        setup.Controller.Open();

        Assert.Equal(1, setup.Window.ShowCalls);
    }

    [Fact]
    public async Task Toggle_starts_a_capture_when_idle_and_stops_it_when_listening()
    {
        var setup = new Setup();
        var (source, started) = Holding();
        setup.Rig.Source = source;

        setup.Controller.ToggleCapture();
        await started.Task.WaitAsync(Wait);
        Assert.Equal(CaptureState.Listening, setup.Rig.Session.State);

        setup.Controller.ToggleCapture();

        Assert.Equal(CaptureState.Done, setup.Rig.Session.State);
    }

    [Fact]
    public async Task Toggle_starts_again_after_a_finished_capture()
    {
        var setup = new Setup();
        var (first, firstStarted) = Holding();
        setup.Rig.Source = first;
        var run = setup.Rig.Session.StartAsync();
        await firstStarted.Task.WaitAsync(Wait);
        setup.Rig.Session.Stop();
        await run.WaitAsync(Wait);
        Assert.Equal(CaptureState.Done, setup.Rig.Session.State);
        var (source, started) = Holding();
        setup.Rig.Source = source;

        setup.Controller.ToggleCapture();
        await started.Task.WaitAsync(Wait);

        Assert.Equal(CaptureState.Listening, setup.Rig.Session.State);
        setup.Controller.ToggleCapture();
    }

    [Fact]
    public async Task Tooltip_and_menu_text_follow_the_state()
    {
        var setup = new Setup();
        Assert.Equal("AQW Companion - not capturing", setup.Controller.ToolTip);
        Assert.Equal("Start capture", setup.Controller.CaptureMenuText);

        var (source, started) = Holding();
        setup.Rig.Source = source;
        var run = setup.Rig.Session.StartAsync();
        await started.Task.WaitAsync(Wait);
        Assert.Equal("AQW Companion - waiting for a login", setup.Controller.ToolTip);
        Assert.Equal("Stop capture", setup.Controller.CaptureMenuText);

        setup.Rig.Session.Stop();
        await run.WaitAsync(Wait);
        Assert.Equal("AQW Companion - not capturing", setup.Controller.ToolTip);
    }

    [Fact]
    public async Task Tooltip_counts_the_characters_captured_in_this_session()
    {
        var setup = new Setup();
        var (source, started) = Holding(Connection(clientPort: 50000, name: "First Hero").All);
        setup.Rig.Source = source;
        var run = setup.Rig.Session.StartAsync();
        await started.Task.WaitAsync(Wait);
        Assert.Equal("AQW Companion - capturing, 1 character", setup.Controller.ToolTip);
        setup.Rig.Session.Stop();
        await run.WaitAsync(Wait);

        var (second, secondStarted) = Holding(Connection(clientPort: 50001, name: "Second Hero").All);
        setup.Rig.Source = second;
        var again = setup.Rig.Session.StartAsync();
        await secondStarted.Task.WaitAsync(Wait);

        Assert.Equal("AQW Companion - capturing, 2 characters", setup.Controller.ToolTip);
        setup.Rig.Session.Stop();
        await again.WaitAsync(Wait);
    }

    [Fact]
    public async Task Tooltip_names_the_states_that_prevent_the_capture()
    {
        var setup = new Setup();
        setup.Rig.Npcap = NpcapStatus.Missing;
        await setup.Rig.Session.StartAsync();
        Assert.Equal("AQW Companion - Npcap is not installed", setup.Controller.ToolTip);

        setup.Rig.Npcap = NpcapStatus.Available;
        setup.Rig.Elevation.IsElevated = false;
        await setup.Rig.Session.StartAsync();
        Assert.Equal("AQW Companion - needs administrator rights", setup.Controller.ToolTip);

        setup.Rig.Elevation.IsElevated = true;
        setup.Rig.Source = new FakePacketSource((_, _) => Task.FromException(new InvalidOperationException()));
        await setup.Rig.Session.StartAsync();
        Assert.Equal("AQW Companion - capture failed", setup.Controller.ToolTip);

        setup.Rig.IsWindows = false;
        await setup.Rig.Session.StartAsync();
        Assert.Equal("AQW Companion - capture not available", setup.Controller.ToolTip);
    }

    [Fact]
    public async Task Changed_is_raised_when_the_session_changes()
    {
        var setup = new Setup();
        var raised = 0;
        setup.Controller.Changed += (_, _) => Interlocked.Increment(ref raised);

        await setup.Rig.Session.StartAsync();

        Assert.True(raised > 0);
    }
}
