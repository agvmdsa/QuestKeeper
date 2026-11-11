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
        public required FakeTailscale Tailscale { get; init; }
        public required FakeServeMarker Marker { get; init; }
        public bool RemovalSucceeds { get; set; } = true;
        public List<FakeTimer> Timers { get; } = [];
        public List<TimeSpan> Intervals { get; } = [];
        public FakeTimer? Live => Timers.LastOrDefault(t => !t.Disposed);
    }

    private static Rig Build(bool saved = false, bool canStart = true, Action<Exception>? onError = null, Action? tickThrows = null, FeedSnapshot? initial = null, FakeTailscale? tailscale = null, FakeServeMarker? marker = null, Action<Action>? onUi = null)
    {
        marker ??= new FakeServeMarker();
        tailscale ??= new FakeTailscale();
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

        var controller = new PhoneController(settings, host, hub, registry, tailscale, marker, timerFactory: Factory, tick: tickThrows, run: work => work(), onUiThread: onUi);
        tailscale.HostRunning = () => host.IsRunning;
        PhoneController? self = controller;
        ForgetCoordinator coordinator = new(() => rig!.RemovalSucceeds, () => [self]);
        rig = new Rig { Clock = clock, Coordinator = coordinator, Settings = settings, Host = host, Controller = controller, Registry = registry, Store = store, Hub = hub, Log = log, SettingsStore = settingsStore, Tailscale = tailscale, Marker = marker };
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

        rig.Coordinator.Forget();

        Assert.True(rig.Host.IsRunning);
        Assert.NotNull(rig.Live);
        Assert.Equal("Listening on 127.0.0.1:47822", rig.Settings.PhoneStatusText);
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

    private const string Dns = FakeTailscale.Dns;

    [Fact]
    public void On_while_ready_starts_the_host_then_serves_once_and_allows_the_name()
    {
        var rig = Build();

        rig.Settings.PhoneAccessEnabled = true;

        Assert.True(rig.Host.IsRunning);
        Assert.Equal([47822], rig.Tailscale.Serves);
        Assert.Equal([true], rig.Tailscale.HostRunningAtServe);
        Assert.True(rig.Controller.AllowedHost(Dns));
        Assert.True(rig.Controller.AllowedHost("127.0.0.1:47822"));
        Assert.True(rig.Controller.AllowedHost("localhost:47822"));
        Assert.False(rig.Controller.AllowedHost("evil.com"));
        Assert.True(rig.Settings.CanPair);
        Assert.Equal(TailscaleService.ReadyText, rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void Starting_twice_serves_once()
    {
        var rig = Build(saved: true);

        rig.Controller.Start();
        rig.Controller.Start();

        Assert.Single(rig.Tailscale.Serves);
    }

    [Theory]
    [InlineData(Dns, true)]
    [InlineData("MY-PC.TAIL1234.TS.NET", true)]
    [InlineData(Dns + ":443", false)]
    [InlineData(Dns + ".evil.com", false)]
    [InlineData("evil.com", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(" " + Dns, false)]
    [InlineData("127.0.0.1:47822", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("localhost:8080", false)]
    public void AllowedHost_table_after_serving(string host, bool allowed)
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;

        Assert.Equal(allowed, rig.Controller.AllowedHost(host));
    }

    [Fact]
    public void AllowedHost_refuses_the_tailnet_name_before_it_was_served()
    {
        var rig = Build();

        Assert.False(rig.Controller.AllowedHost(Dns));
        Assert.True(rig.Controller.AllowedHost("localhost:47822"));
    }

    [Fact]
    public void A_name_whose_serve_failed_is_never_allowed()
    {
        var rig = Build(tailscale: new FakeTailscale { ServeResult = false });

        rig.Settings.PhoneAccessEnabled = true;

        Assert.False(rig.Controller.AllowedHost(Dns));
        Assert.False(rig.Settings.CanPair);
        Assert.Equal(PhoneController.ServeFailedText, rig.Settings.PhoneTailscaleText);
        Assert.StartsWith("Could not publish the phone page through Tailscale.", rig.Settings.PhoneTailscaleText);
        Assert.True(rig.Host.IsRunning);
        Assert.Null(rig.Controller.CreatePairingLink());
    }

    [Fact]
    public void Off_stops_the_serve_and_drops_the_name()
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;

        rig.Settings.PhoneAccessEnabled = false;

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.False(rig.Controller.AllowedHost(Dns));
        Assert.False(rig.Settings.CanPair);
        Assert.Equal(string.Empty, rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void Off_from_the_start_never_touches_tailscale()
    {
        var rig = Build();

        rig.Controller.Start();

        Assert.Equal(0, rig.Tailscale.Stops);
        Assert.Equal(0, rig.Tailscale.StateCalls);
    }

    [Theory]
    [InlineData(TailscaleStateKind.NotInstalled)]
    [InlineData(TailscaleStateKind.NotSignedIn)]
    [InlineData(TailscaleStateKind.HttpsNotEnabled)]
    [InlineData(TailscaleStateKind.Error)]
    public void A_state_that_is_not_ready_starts_the_local_host_but_never_serves(TailscaleStateKind kind)
    {
        var sentence = $"sentence for {kind}";
        var rig = Build(tailscale: new FakeTailscale { State = new TailscaleState(kind, null, sentence) });

        rig.Settings.PhoneAccessEnabled = true;

        Assert.True(rig.Host.IsRunning);
        Assert.Empty(rig.Tailscale.Serves);
        Assert.False(rig.Settings.CanPair);
        Assert.Equal(sentence, rig.Settings.PhoneTailscaleText);
        Assert.Equal(kind, rig.Controller.TailscaleState?.Kind);
        Assert.Null(rig.Controller.CreatePairingLink());
        Assert.False(rig.Controller.AllowedHost(Dns));
    }

    [Fact]
    public void Opening_the_dialog_refreshes_the_state_and_picks_up_a_change()
    {
        var rig = Build(tailscale: new FakeTailscale { State = new TailscaleState(TailscaleStateKind.NotSignedIn, null, "sign in") });
        rig.Settings.PhoneAccessEnabled = true;
        var before = rig.Tailscale.StateCalls;
        Assert.False(rig.Settings.CanPair);

        rig.Tailscale.State = FakeTailscale.Ready();
        rig.Settings.Refresh();

        Assert.Equal(before + 1, rig.Tailscale.StateCalls);
        Assert.True(rig.Settings.CanPair);
        Assert.Single(rig.Tailscale.Serves);
    }

    [Fact]
    public void Refreshing_a_published_page_does_not_serve_again_and_notices_a_lost_ready()
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;

        rig.Settings.Refresh();
        Assert.Single(rig.Tailscale.Serves);

        rig.Tailscale.State = new TailscaleState(TailscaleStateKind.NotSignedIn, null, "sign in");
        rig.Settings.Refresh();

        Assert.False(rig.Settings.CanPair);
        Assert.False(rig.Controller.AllowedHost(Dns));
        Assert.Null(rig.Controller.CreatePairingLink());
    }

    [Fact]
    public void Refresh_with_the_switch_off_does_not_call_tailscale()
    {
        var rig = Build();

        rig.Settings.Refresh();

        Assert.Equal(0, rig.Tailscale.StateCalls);
    }

    [Fact]
    public void CreatePairingLink_is_null_when_the_host_is_not_running()
    {
        var rig = Build();

        Assert.Null(rig.Controller.CreatePairingLink());
    }

    [Fact]
    public void CreatePairingLink_gives_a_link_whose_token_the_registry_accepts_exactly_once()
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;

        var link = rig.Controller.CreatePairingLink();

        Assert.NotNull(link);
        var prefix = $"https://{Dns}/#t=";
        Assert.StartsWith(prefix, link);
        var token = link![prefix.Length..];
        Assert.Equal(PairStatus.Paired, rig.Registry.TryPair(token, "Phone").Status);
        Assert.Equal(PairStatus.Invalid, rig.Registry.TryPair(token, "Phone").Status);
    }

    [Fact]
    public void Tailscale_failures_never_escape_the_controller()
    {
        var rig = Build(tailscale: new FakeTailscale { Throws = new InvalidOperationException("secret detail") });

        var ex = Record.Exception(() =>
        {
            rig.Settings.PhoneAccessEnabled = true;
            rig.Settings.Refresh();
            rig.Controller.Quiesce();
            rig.Controller.Forget();
            rig.Settings.PhoneAccessEnabled = false;
        });

        Assert.Null(ex);
        Assert.False(rig.Settings.CanPair);
        Assert.False(rig.Controller.AllowedHost(Dns));
    }

    [Fact]
    public void Quiesce_stops_the_serve_and_drops_the_name()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();

        rig.Controller.Quiesce();

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.False(rig.Controller.AllowedHost(Dns));
        Assert.False(rig.Settings.CanPair);
    }

    [Fact]
    public void Forget_stops_the_serve_and_serves_again_when_the_switch_is_on()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();

        rig.Controller.Forget();

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.Equal(2, rig.Tailscale.Serves.Count);
        Assert.True(rig.Controller.AllowedHost(Dns));
        Assert.True(rig.Settings.CanPair);
    }

    [Fact]
    public void Forget_with_the_switch_off_does_not_serve()
    {
        var rig = Build();
        rig.Controller.Start();

        rig.Controller.Forget();

        Assert.Empty(rig.Tailscale.Serves);
        Assert.False(rig.Controller.AllowedHost(Dns));
    }

    [Fact]
    public void A_host_that_cannot_start_never_serves()
    {
        var rig = Build(canStart: false);

        rig.Settings.PhoneAccessEnabled = true;

        Assert.Empty(rig.Tailscale.Serves);
        Assert.False(rig.Settings.CanPair);
    }

    [Fact]
    public void The_name_is_not_allowed_while_the_serve_is_in_flight_and_is_after_it_succeeded()
    {
        var seenAllowedDuringServe = new List<bool>();
        var tailscale = new FakeTailscale();
        var rig = Build(tailscale: tailscale);
        tailscale.OnServe = () => seenAllowedDuringServe.Add(rig.Controller.AllowedHost(Dns));

        rig.Settings.PhoneAccessEnabled = true;

        Assert.Equal([false], seenAllowedDuringServe);
        Assert.True(rig.Controller.AllowedHost(Dns));
    }

    [Fact]
    public async Task On_then_off_while_the_serve_is_pending_stops_once_and_never_allows_the_name()
    {
        var gate = new TaskCompletionSource<bool>();
        var rig = Build(tailscale: new FakeTailscale { ServeGate = gate });
        rig.Settings.PhoneAccessEnabled = true;
        Assert.False(rig.Controller.AllowedHost(Dns));

        rig.Settings.PhoneAccessEnabled = false;
        gate.SetResult(true);
        await Wait.Until(() => rig.Tailscale.Stops == 1);

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.False(rig.Controller.AllowedHost(Dns));
        Assert.False(rig.Settings.CanPair);
        Assert.Equal(string.Empty, rig.Settings.PhoneTailscaleText);
        Assert.False(rig.Marker.Present);
    }

    [Fact]
    public async Task Forget_while_the_serve_is_pending_stops_once_and_serves_again()
    {
        var gate = new TaskCompletionSource<bool>();
        var rig = Build(tailscale: new FakeTailscale { ServeGate = gate });
        rig.Settings.PhoneAccessEnabled = true;

        rig.Controller.Forget();
        gate.SetResult(true);
        await Wait.Until(() => rig.Tailscale.Serves.Count == 2);

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.True(rig.Controller.AllowedHost(Dns));
        Assert.True(rig.Settings.CanPair);
    }

    [Fact]
    public void The_text_is_judged_again_on_the_ui_thread_so_a_switch_off_leaves_no_ready_on_screen()
    {
        var queued = new List<Action>();
        var tailscale = new FakeTailscale();
        var rig = Build(tailscale: tailscale, onUi: queued.Add);

        rig.Settings.PhoneAccessEnabled = true;
        rig.Settings.PhoneAccessEnabled = false;
        foreach (var action in queued.ToList())
        {
            action();
        }

        Assert.False(rig.Settings.CanPair);
        Assert.Equal(string.Empty, rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void A_name_with_a_trailing_dot_and_capitals_is_stored_normalised()
    {
        var rig = Build(tailscale: new FakeTailscale { State = FakeTailscale.Ready("My-PC.Tail1234.ts.net.") });
        rig.Settings.PhoneAccessEnabled = true;

        Assert.True(rig.Controller.AllowedHost("my-pc.tail1234.ts.net"));
        var link = rig.Controller.CreatePairingLink();
        Assert.StartsWith("https://my-pc.tail1234.ts.net/#t=", link);
    }

    [Fact]
    public void The_marker_is_made_before_the_serve_and_removed_after_a_successful_stop()
    {
        var tailscale = new FakeTailscale();
        var rig = Build(tailscale: tailscale);
        var markerAtServe = new List<bool>();
        tailscale.OnServe = () => markerAtServe.Add(rig.Marker.Present);

        rig.Settings.PhoneAccessEnabled = true;
        Assert.Equal([true], markerAtServe);
        Assert.True(rig.Marker.Present);

        rig.Settings.PhoneAccessEnabled = false;
        Assert.False(rig.Marker.Present);
    }

    [Fact]
    public void A_failed_stop_keeps_the_marker_says_so_and_the_next_apply_retries()
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;
        rig.Tailscale.StopResult = false;

        rig.Settings.PhoneAccessEnabled = false;

        Assert.True(rig.Marker.Present);
        Assert.Equal(PhoneController.StopFailedText, rig.Settings.PhoneTailscaleText);
        Assert.Equal("Could not remove the phone page from Tailscale. It will be tried again.", PhoneController.StopFailedText);

        rig.Tailscale.StopResult = true;
        rig.Settings.Refresh();

        Assert.Equal(2, rig.Tailscale.Stops);
        Assert.False(rig.Marker.Present);
        Assert.Equal(string.Empty, rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void After_a_crash_the_next_start_with_the_switch_off_removes_the_route_and_the_marker()
    {
        var marker = new FakeServeMarker(exists: true);
        var rig = Build(marker: marker);

        rig.Controller.Start();

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.False(marker.Present);
        Assert.Empty(rig.Tailscale.Serves);
    }

    [Fact]
    public void A_failed_cleanup_at_start_keeps_the_marker()
    {
        var marker = new FakeServeMarker(exists: true);
        var rig = Build(marker: marker, tailscale: new FakeTailscale { StopResult = false });

        rig.Controller.Start();

        Assert.True(marker.Present);
        Assert.Equal(PhoneController.StopFailedText, rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void Without_a_marker_start_never_stops_anything()
    {
        var rig = Build();

        rig.Controller.Start();

        Assert.Equal(0, rig.Tailscale.Stops);
    }

    [Fact]
    public void Dispose_waits_for_a_slow_stop()
    {
        var gate = new TaskCompletionSource<bool>();
        var rig = Build(saved: true, tailscale: new FakeTailscale { StopGate = gate });
        rig.Controller.Start();
        _ = Task.Delay(300).ContinueWith(_ => gate.SetResult(true));

        rig.Controller.Dispose();

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.False(rig.Marker.Present);
    }

    [Fact]
    public void Dispose_gives_up_after_the_bounded_wait_and_never_throws()
    {
        var rig = Build(saved: true, tailscale: new FakeTailscale { StopGate = new TaskCompletionSource<bool>() });
        rig.Controller.Start();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var ex = Record.Exception(rig.Controller.Dispose);

        Assert.Null(ex);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(9));
        Assert.True(rig.Marker.Present);
    }

    private static TailscaleState NotInstalled() => new(TailscaleStateKind.NotInstalled, null, TailscaleService.NotInstalledText);

    [Fact]
    public void A_route_believed_published_when_tailscale_is_gone_is_forgotten_with_the_switch_on()
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;
        Assert.True(rig.Marker.Present);

        // Uninstalled: there is no tailscale.exe to reset with, so the real service fails every stop.
        rig.Tailscale.State = NotInstalled();
        rig.Tailscale.StopResult = false;
        rig.Settings.Refresh();

        Assert.False(rig.Marker.Present);
        Assert.Equal(TailscaleService.NotInstalledText, rig.Settings.PhoneTailscaleText);

        rig.Settings.Refresh();
        Assert.Equal(TailscaleService.NotInstalledText, rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void A_marker_left_behind_when_tailscale_is_gone_is_removed_at_start_with_the_switch_off()
    {
        var marker = new FakeServeMarker(exists: true);
        var rig = Build(marker: marker, tailscale: new FakeTailscale { State = NotInstalled(), StopResult = false });

        rig.Controller.Start();

        Assert.False(marker.Present);
        Assert.Equal(string.Empty, rig.Settings.PhoneTailscaleText);

        rig.Settings.Refresh();
        Assert.Equal(1, rig.Tailscale.Stops);
    }

    [Fact]
    public void A_failed_stop_followed_by_an_uninstall_does_not_say_could_not_remove_forever()
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;
        rig.Tailscale.StopResult = false;
        rig.Settings.PhoneAccessEnabled = false;
        Assert.Equal(PhoneController.StopFailedText, rig.Settings.PhoneTailscaleText);

        rig.Tailscale.State = NotInstalled();
        rig.Settings.PhoneAccessEnabled = true;

        Assert.False(rig.Marker.Present);
        Assert.Equal(TailscaleService.NotInstalledText, rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void A_failed_stale_route_stop_with_the_switch_on_says_so_and_a_refresh_retries()
    {
        var rig = Build();
        rig.Settings.PhoneAccessEnabled = true;
        rig.Tailscale.State = new TailscaleState(TailscaleStateKind.NotSignedIn, null, "sign in");
        rig.Tailscale.StopResult = false;

        rig.Settings.Refresh();

        Assert.Equal(1, rig.Tailscale.Stops);
        Assert.True(rig.Marker.Present);
        Assert.Equal(PhoneController.StopFailedText, rig.Settings.PhoneTailscaleText);
        Assert.False(rig.Settings.CanPair);

        rig.Tailscale.StopResult = true;
        rig.Settings.Refresh();

        Assert.Equal(2, rig.Tailscale.Stops);
        Assert.False(rig.Marker.Present);
        Assert.Equal("sign in", rig.Settings.PhoneTailscaleText);
    }

    [Fact]
    public void A_throwing_registry_forget_still_brings_the_host_back_and_never_escapes()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();
        rig.Store.DeleteThrows = new IOException("disk gone");
        rig.Controller.Quiesce();
        Assert.False(rig.Host.IsRunning);

        var ex = Record.Exception(rig.Controller.Forget);

        Assert.Null(ex);
        Assert.True(rig.Host.IsRunning);
        Assert.Equal("Listening on 127.0.0.1:47822", rig.Settings.PhoneStatusText);
        Assert.Empty(rig.Hub.Current.Notices);

        rig.Settings.PhoneAccessEnabled = false;
        Assert.False(rig.Host.IsRunning);
    }

    [Fact]
    public void A_throwing_registry_forget_through_the_coordinator_leaves_the_switch_working()
    {
        var rig = Build(saved: true);
        rig.Controller.Start();
        rig.Store.DeleteThrows = new IOException("disk gone");

        rig.Coordinator.Forget();

        Assert.True(rig.Host.IsRunning);
        Assert.NotNull(rig.Live);
        Assert.Equal("Listening on 127.0.0.1:47822", rig.Settings.PhoneStatusText);
    }
}
