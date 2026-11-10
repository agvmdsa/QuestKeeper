using AqwCompanion.App.Phone;
using AqwCompanion.Core.Feed;
using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneControllerTests
{
    private sealed class FakeTimer(Action tick) : IDisposable
    {
        public Action Tick { get; } = tick;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class Rig
    {
        public required SettingsViewModel Settings { get; init; }
        public required FakePhoneHost Host { get; init; }
        public required PhoneController Controller { get; init; }
        public required PhoneRegistry Registry { get; init; }
        public required MemoryPhoneFileStore Store { get; init; }
        public required PhoneHub Hub { get; init; }
        public required NoticeLog Log { get; init; }
        public required FakeAppSettingsStore SettingsStore { get; init; }
        public required ManualClock Clock { get; init; }
        public required ForgetCoordinator Coordinator { get; init; }
        public bool RemovalSucceeds { get; set; } = true;
        public List<FakeTimer> Timers { get; } = [];
        public List<TimeSpan> Intervals { get; } = [];
        public FakeTimer? Live => Timers.LastOrDefault(t => !t.Disposed);
    }

    private static Rig Build(bool saved = false, bool canStart = true, Action<Exception>? onError = null, Action? tickThrows = null, FeedSnapshot? initial = null)
    {
        var settingsStore = new FakeAppSettingsStore(new AppSettings(PhoneAccess: saved));
        var settings = new SettingsViewModel(settingsStore, new ForgetCoordinator(() => true, () => []));
        var host = new FakePhoneHost { CanStart = canStart };
        var store = new MemoryPhoneFileStore();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        var registry = new PhoneRegistry(store, clock.GetUtcNow);
        var log = new NoticeLog(50);
        var hub = new PhoneHub(new FakeFeedSource(initial), new NoticeEngine(), log, clock.GetUtcNow, onError);
        Rig? rig = null;
        IDisposable Factory(TimeSpan interval, Action tick)
        {
            var timer = new FakeTimer(tick);
            rig!.Timers.Add(timer);
            rig.Intervals.Add(interval);
            return timer;
        }

        var controller = new PhoneController(settings, host, hub, registry, timerFactory: Factory, tick: tickThrows);
        PhoneController? self = controller;
        ForgetCoordinator coordinator = new(() => rig!.RemovalSucceeds, () => [self]);
        rig = new Rig { Clock = clock, Coordinator = coordinator, Settings = settings, Host = host, Controller = controller, Registry = registry, Store = store, Hub = hub, Log = log, SettingsStore = settingsStore };
        return rig;
    }

    [Fact]
    public void Off_by_default_it_starts_nothing_and_says_off()
    {
        var rig = Build();

        rig.Controller.Start();

        Assert.Equal(0, rig.Host.Starts);
        Assert.Equal("Off", rig.Settings.PhoneStatusText);
        Assert.Empty(rig.Timers);
    }

    [Fact]
    public void A_saved_on_setting_starts_the_host_at_start()
    {
        var rig = Build(saved: true);

        rig.Controller.Start();

        Assert.True(rig.Host.IsRunning);
        Assert.Equal("Listening on 127.0.0.1:47822", rig.Settings.PhoneStatusText);
        Assert.True(rig.Registry.Enabled);
    }

    [Fact]
    public void The_switch_starts_and_stops_the_host_and_keeps_the_registry_in_step()
    {
        var rig = Build();

        rig.Settings.PhoneAccessEnabled = true;
        Assert.True(rig.Host.IsRunning);
        Assert.True(rig.Registry.Enabled);

        rig.Settings.PhoneAccessEnabled = false;
        Assert.False(rig.Host.IsRunning);
        Assert.False(rig.Registry.Enabled);
        Assert.Equal("Off", rig.Settings.PhoneStatusText);
    }

    [Fact]
    public void A_host_that_cannot_start_is_said_in_words_and_nothing_runs()
    {
        var rig = Build(canStart: false);

        rig.Settings.PhoneAccessEnabled = true;

        Assert.False(rig.Host.IsRunning);
        Assert.Equal("Could not listen on 127.0.0.1:47822 (the port may be in use, or it is not allowed)", rig.Settings.PhoneStatusText);
        Assert.Equal(rig.Controller.FailedText, rig.Settings.PhoneStatusText);
        Assert.Null(rig.Live);
    }

    [Fact]
    public void Starting_twice_starts_one_listener_and_one_timer()
    {
        var rig = Build(saved: true);

        rig.Controller.Start();
        rig.Controller.Start();

        Assert.Equal(1, rig.Host.Starts);
        Assert.Single(rig.Timers);
    }

    [Fact]
    public void The_tick_timer_runs_every_15_seconds_only_while_the_host_runs()
    {
        var rig = Build();
        rig.Controller.Start();
        Assert.Empty(rig.Timers);

        rig.Settings.PhoneAccessEnabled = true;
        Assert.NotNull(rig.Live);
        Assert.Equal(TimeSpan.FromSeconds(15), rig.Intervals[0]);

        rig.Settings.PhoneAccessEnabled = false;
        Assert.Null(rig.Live);
    }

    [Fact]
    public void A_throwing_tick_never_escapes_the_timer()
    {
        var calls = 0;
        var rig = Build(tickThrows: () =>
        {
            calls++;
            throw new InvalidOperationException("secret detail");
        });
        rig.Settings.PhoneAccessEnabled = true;

        var ex = Record.Exception(() => rig.Live!.Tick());

        Assert.Null(ex);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void The_tick_timer_drives_the_hub_so_a_stalled_character_is_announced()
    {
        var start = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        var hero = new FeedCharacter("Alpha", 60, true, start, start, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null);
        var rig = Build(initial: new FeedSnapshot(1, new FeedCapture("Listening.", true), "Alpha", [hero], [], null));
        rig.Settings.PhoneAccessEnabled = true;
        Assert.Empty(rig.Hub.Current.Notices);

        rig.Clock.Advance(TimeSpan.FromHours(1));
        rig.Live!.Tick();

        Assert.Contains(rig.Hub.Current.Notices, n => n.Kind == NoticeKind.ActivityStopped);
    }

    [Fact]
    public void Quiesce_alone_says_off_and_stops_the_host()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();

        rig.Controller.Quiesce();

        Assert.False(rig.Host.IsRunning);
        Assert.Equal("Off", rig.Settings.PhoneStatusText);
    }

    [Fact]
    public void After_a_successful_forget_the_host_comes_back_clean_and_the_switch_still_works()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();
        rig.Registry.TryPair(rig.Registry.CreatePairingToken(), "Phone");

        var result = rig.Coordinator.Forget();

        Assert.True(result.IsComplete);
        Assert.True(rig.Host.IsRunning);
        Assert.NotNull(rig.Live);
        Assert.Equal("Listening on 127.0.0.1:47822", rig.Settings.PhoneStatusText);
        Assert.Empty(rig.Registry.List());
        Assert.Empty(rig.Hub.Current.Notices);

        rig.Settings.PhoneAccessEnabled = false;
        Assert.False(rig.Host.IsRunning);
        rig.Settings.PhoneAccessEnabled = true;
        Assert.True(rig.Host.IsRunning);
    }

    [Fact]
    public void A_failed_removal_resumes_the_host_and_keeps_the_devices()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();
        rig.Registry.TryPair(rig.Registry.CreatePairingToken(), "Phone");
        rig.RemovalSucceeds = false;

        rig.Coordinator.Forget();

        Assert.True(rig.Host.IsRunning);
        Assert.Equal("Listening on 127.0.0.1:47822", rig.Settings.PhoneStatusText);
        Assert.Single(rig.Registry.List());
    }

    [Fact]
    public void Forget_with_the_switch_off_leaves_the_host_stopped_and_says_off()
    {
        var rig = Build();
        rig.Controller.Start();

        rig.Coordinator.Forget();

        Assert.False(rig.Host.IsRunning);
        Assert.Equal("Off", rig.Settings.PhoneStatusText);
    }

    [Fact]
    public void Dispose_stops_the_timer_and_the_host()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();

        rig.Controller.Dispose();

        Assert.Null(rig.Live);
        Assert.False(rig.Host.IsRunning);
    }

    [Fact]
    public void Quiesce_stops_the_host_and_the_tick_without_changing_the_saved_switch_and_resume_restarts_it()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();

        rig.Controller.Quiesce();
        Assert.False(rig.Host.IsRunning);
        Assert.Null(rig.Live);
        Assert.True(rig.Settings.PhoneAccessEnabled);
        Assert.True(rig.SettingsStore.Current.PhoneAccess);

        rig.Controller.Resume();
        Assert.True(rig.Host.IsRunning);
        Assert.NotNull(rig.Live);
    }

    [Fact]
    public void Resume_does_not_start_a_host_when_the_switch_is_off()
    {
        var rig = Build();
        rig.Controller.Start();

        rig.Controller.Quiesce();
        rig.Controller.Resume();

        Assert.Equal(0, rig.Host.Starts);
    }

    [Fact]
    public void Forget_revokes_the_devices_resets_the_hub_and_keeps_the_switch()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();
        rig.Registry.TryPair(rig.Registry.CreatePairingToken(), "Phone");
        Assert.Single(rig.Registry.List());

        rig.Controller.Forget();

        Assert.Equal(1, rig.Store.Deletes);
        Assert.Empty(rig.Registry.List());
        Assert.Empty(rig.Hub.Current.Notices);
        Assert.True(rig.Settings.PhoneAccessEnabled);
        Assert.Equal("Phone access", rig.Controller.Name);
    }
}
