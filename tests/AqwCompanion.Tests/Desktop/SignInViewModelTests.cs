using AqwCompanion.App;
using AqwCompanion.App.SignIn;
using AqwCompanion.App.ViewModels;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace AqwCompanion.Tests.Desktop;

public class SignInViewModelTests
{
    private const string LoginUrl = "https://account.aq.com/Login";

    private readonly FakeBrowserHost _host = new();
    private readonly FakeDelay _delay = new();
    private readonly FailedSnapshotBuffer _failures = new();
    private readonly LatestResultStore<AccountIdentity> _store = new();

    private readonly CollectingSink _logs = new();

    private SignInViewModel Build(
        SignInMode mode = SignInMode.Full,
        TimeSpan? readTimeout = null,
        TimeSpan? navigationTimeout = null)
    {
        var vm = new SignInViewModel(
            _host,
            new IngestionService<AccountIdentity>(
                new AccountHomeParser(), _store, _failures, new ListLogger<IngestionService<AccountIdentity>>()),
            _delay,
            mode,
            readTimeout,
            navigationTimeout: navigationTimeout,
            logger: new LoggerConfiguration().WriteTo.Sink(_logs).CreateLogger());
        vm.Start();
        return vm;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }

    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Slow = TimeSpan.FromMinutes(5);

    private static PageSnapshot Page(string fixture, string url = AqwUrls.Home) =>
        PageSnapshot.Create(
            url,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)),
            "1.0.0",
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void Full_mode_starts_by_opening_account_home_and_waiting_for_the_user()
    {
        var vm = Build();

        Assert.Equal(AqwUrls.Home, _host.LastNavigated);
        Assert.Equal(1, _host.NavigateCount);
        Assert.Equal(SignInState.SigningIn, vm.State);
        Assert.True(vm.IsFull);
        Assert.False(vm.IsCompact);
        Assert.Equal(SignInViewModel.SigningInMessage, vm.Message);
        Assert.Equal(SignInViewModel.FullTitle, vm.WindowTitle);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void Compact_mode_starts_with_the_updating_title()
    {
        var vm = Build(SignInMode.Compact);

        Assert.True(vm.IsCompact);
        Assert.Equal("Updating from account.aq.com...", vm.WindowTitle);
        Assert.Equal("Updating from account.aq.com...", vm.CompactTitle);
        Assert.True(vm.ShowCompactProgress);
        Assert.False(vm.ShowFullStatus);
        Assert.Equal(AqwUrls.Home, _host.LastNavigated);
    }

    [Fact]
    public void Starting_twice_navigates_only_once()
    {
        var vm = Build();

        vm.Start();

        Assert.Equal(1, _host.NavigateCount);
        Assert.Equal(1, _host.Subscribers);
    }

    [Theory]
    [InlineData("https://evil.example/Home")]
    [InlineData("https://account.aq.com.evil.example/Home")]
    [InlineData("http://account.aq.com/Home")]
    [InlineData("https://www.aq.com/Home")]
    [InlineData("not a url")]
    public async Task Pages_outside_account_aq_com_are_never_scripted_or_parsed(string url)
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html", url);

        await vm.HandleNavigationAsync(url);

        Assert.Equal(0, _host.CaptureCount);
        Assert.Equal(SignInState.SigningIn, vm.State);
        Assert.Null(vm.CharacterName);
        Assert.Null(_failures.Latest);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task A_foreign_first_page_in_a_compact_refresh_switches_to_the_full_layout_with_the_expired_message()
    {
        var vm = Build(SignInMode.Compact);

        await vm.HandleNavigationAsync("https://sso.example/login");

        Assert.True(vm.IsFull);
        Assert.Equal(SignInState.SessionExpired, vm.State);
        Assert.Equal(SignInViewModel.SessionExpiredMessage, vm.Message);
        Assert.Equal(0, _host.CaptureCount);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task Navigation_to_a_non_home_account_page_in_full_mode_just_keeps_waiting()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-login.html", LoginUrl);

        await vm.HandleNavigationAsync(LoginUrl);

        Assert.Equal(0, _host.CaptureCount);
        Assert.Equal(SignInState.SigningIn, vm.State);
        Assert.True(vm.IsFull);
        Assert.Null(vm.Result);
        Assert.Null(_failures.Latest);
    }

    [Fact]
    public async Task A_refresh_that_lands_on_the_login_page_switches_to_the_full_layout_with_the_expired_message()
    {
        var vm = Build(SignInMode.Compact);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.HandleNavigationAsync(LoginUrl);

        Assert.Equal(SignInMode.Full, vm.Mode);
        Assert.Equal(SignInState.SessionExpired, vm.State);
        Assert.Equal("Your session expired. Please sign in again.", vm.Message);
        Assert.Equal(0, _host.CaptureCount);
        Assert.True(vm.ShowFullStatus);
        Assert.False(vm.ShowCompactProgress);
        Assert.Contains(nameof(SignInViewModel.IsFull), changed);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task After_the_session_expired_the_user_signing_in_completes_the_flow()
    {
        var vm = Build(SignInMode.Compact);
        await vm.HandleNavigationAsync(LoginUrl);
        _host.OnCapture = () => Page("account-home-sample.html");

        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal(SignInState.Success, vm.State);
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
    }

    [Fact]
    public void Navigation_completed_on_home_reads_the_name_shows_connected_and_closes()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        var closed = new List<SignInResult>();
        vm.CloseRequested += (_, r) => closed.Add(r);

        _host.Raise(AqwUrls.Home);

        Assert.Equal(1, _host.CaptureCount);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal(SignInState.Success, vm.State);
        Assert.Equal("Connected as Test Hero", vm.SuccessText);
        Assert.Equal("Connected as Test Hero", vm.Message);
        Assert.Equal(new AccountIdentity("Test Hero"), _store.Latest);
        Assert.Null(_failures.Latest);
        Assert.Equal(SignInResult.Connected("Test Hero"), Assert.Single(closed));
        Assert.Equal(SignInOutcome.Connected, vm.Result!.Outcome);
        Assert.Equal(0, _host.Subscribers);
    }

    [Fact]
    public async Task Connected_stays_visible_for_the_success_delay_before_the_window_closes()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        _delay.Gate = new TaskCompletionSource();
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        var running = vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal(SignInState.Success, vm.State);
        Assert.True(vm.IsSuccess);
        Assert.True(vm.ShowFullSuccess);
        Assert.False(vm.ShowBrowser);
        Assert.Equal(0, closed);
        Assert.Null(vm.Result);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), Assert.Single(_delay.Delays));

        _delay.Gate.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, closed);
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
    }

    [Fact]
    public async Task Home_events_during_the_connected_pause_are_ignored()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        _delay.Gate = new TaskCompletionSource();
        var running = vm.HandleNavigationAsync(AqwUrls.Home);

        await vm.HandleNavigationAsync(AqwUrls.Home);
        await vm.HandleNavigationAsync(LoginUrl);

        Assert.Equal(1, _host.CaptureCount);
        Assert.Equal(SignInState.Success, vm.State);
        _delay.Gate.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(SignInOutcome.Connected, vm.Result!.Outcome);
    }

    [Fact]
    public async Task Dismissing_during_the_connected_pause_still_reports_the_name_that_was_read()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        _delay.Gate = new TaskCompletionSource();
        var running = vm.HandleNavigationAsync(AqwUrls.Home);

        vm.CancelCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
        Assert.True(_delay.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Failed_parse_stays_open_with_the_help_message_and_buffers_the_snapshot()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-drifted.html");

        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal(SignInState.Error, vm.State);
        Assert.Equal(SignInViewModel.ReadFailedMessage, vm.Message);
        Assert.Null(vm.CharacterName);
        Assert.NotNull(_failures.Latest);
        Assert.Null(vm.Result);
        Assert.True(vm.IsError);
        Assert.True(vm.CanTryAgain);
    }

    [Fact]
    public async Task A_failed_compact_refresh_switches_to_the_full_layout_to_show_the_message()
    {
        var vm = Build(SignInMode.Compact);
        _host.OnCapture = () => Page("account-home-drifted.html");

        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.True(vm.IsFull);
        Assert.Equal(SignInState.Error, vm.State);
    }

    [Fact]
    public async Task Closing_after_an_error_reports_the_failure_message()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-drifted.html");
        await vm.HandleNavigationAsync(AqwUrls.Home);

        vm.CancelCommand.Execute(null);

        Assert.Equal(SignInResult.Failed(SignInViewModel.ReadFailedMessage), vm.Result);
    }

    [Fact]
    public async Task Capture_exception_is_reported_as_an_error_without_crashing()
    {
        var vm = Build();
        _host.OnCapture = () => throw new InvalidOperationException("script failed");

        var error = await Record.ExceptionAsync(() => vm.HandleNavigationAsync(AqwUrls.Home));

        Assert.Null(error);
        Assert.Equal(SignInState.Error, vm.State);
        Assert.Null(vm.CharacterName);
        Assert.StartsWith("Could not capture the page", vm.Message, StringComparison.Ordinal);
        Assert.Contains("script failed", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_exception_raised_through_the_event_does_not_escape()
    {
        var vm = Build();

        var error = Record.Exception(() => _host.Raise(AqwUrls.Home));

        Assert.Null(error);
        Assert.StartsWith("Could not capture the page", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_failed_message_is_honest_about_the_diagnostics_steps()
    {
        var message = SignInViewModel.ReadFailedMessage;

        Assert.DoesNotContain("redacted snapshot", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Diagnostics", message, StringComparison.Ordinal);
        Assert.Contains("Include the raw page snapshot of the last failed capture", message, StringComparison.Ordinal);
        Assert.Contains("best-effort", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_that_never_completes_times_out_and_try_again_works()
    {
        var vm = Build(readTimeout: TimeSpan.FromMilliseconds(50));
        _host.OnCaptureAsync = _ => new TaskCompletionSource<PageSnapshot>().Task;

        await vm.HandleNavigationAsync(AqwUrls.Home).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(SignInState.Error, vm.State);
        Assert.Equal(SignInViewModel.ReadTimedOutMessage, vm.Message);
        Assert.Null(vm.Result);

        vm.TryAgainCommand.Execute(null);

        Assert.Equal(2, _host.NavigateCount);
        Assert.Equal(AqwUrls.Home, _host.LastNavigated);
        Assert.Equal(SignInState.SigningIn, vm.State);
        Assert.False(vm.IsError);

        _host.OnCaptureAsync = null;
        _host.OnCapture = () => Page("account-home-sample.html");
        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal(2, _host.CaptureCount);
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
    }

    [Fact]
    public async Task The_capture_is_given_a_token_that_is_cancelled_on_timeout()
    {
        var vm = Build(readTimeout: TimeSpan.FromMilliseconds(50));
        CancellationToken seen = default;
        _host.OnCaptureAsync = ct =>
        {
            seen = ct;
            return new TaskCompletionSource<PageSnapshot>().Task;
        };

        await vm.HandleNavigationAsync(AqwUrls.Home).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task A_home_event_during_a_read_triggers_exactly_one_extra_read()
    {
        var vm = Build();
        var gate = new TaskCompletionSource();
        var first = true;
        _host.OnCaptureAsync = async _ =>
        {
            if (first)
            {
                first = false;
                await gate.Task;
            }

            return Page("account-home-sample.html");
        };

        var running = vm.HandleNavigationAsync(AqwUrls.Home);
        await vm.HandleNavigationAsync(AqwUrls.Home);
        await vm.HandleNavigationAsync(AqwUrls.Home);
        Assert.Equal(1, _host.CaptureCount);

        gate.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, _host.CaptureCount);
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
    }

    [Fact]
    public async Task Leaving_home_during_a_read_cancels_the_pending_re_read()
    {
        var vm = Build();
        var gate = new TaskCompletionSource();
        var first = true;
        _host.OnCaptureAsync = async _ =>
        {
            if (first)
            {
                first = false;
                await gate.Task;
            }

            return Page("account-home-sample.html");
        };
        var running = vm.HandleNavigationAsync(AqwUrls.Home);
        await vm.HandleNavigationAsync(AqwUrls.Home);

        await vm.HandleNavigationAsync(LoginUrl);
        gate.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, _host.CaptureCount);
    }

    [Fact]
    public async Task A_failing_read_still_releases_the_guard()
    {
        var vm = Build();
        _host.OnCapture = () => throw new InvalidOperationException("boom");
        await vm.HandleNavigationAsync(AqwUrls.Home);
        _host.OnCapture = () => Page("account-home-sample.html");

        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal(2, _host.CaptureCount);
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
    }

    [Fact]
    public async Task The_error_stays_visible_when_the_page_moves_on()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-drifted.html");
        await vm.HandleNavigationAsync(AqwUrls.Home);

        await vm.HandleNavigationAsync(LoginUrl);

        Assert.Equal(SignInState.Error, vm.State);
        Assert.Equal(SignInViewModel.ReadFailedMessage, vm.Message);
    }

    [Fact]
    public void Cancelling_while_signing_in_closes_with_a_cancelled_result_and_stops_listening()
    {
        var vm = Build();
        var closed = new List<SignInResult>();
        vm.CloseRequested += (_, r) => closed.Add(r);

        vm.CancelCommand.Execute(null);
        vm.CancelCommand.Execute(null);

        Assert.Equal(SignInResult.Cancelled(), Assert.Single(closed));
        Assert.Equal(SignInState.Cancelled, vm.State);
        Assert.Null(vm.CharacterName);
        Assert.Equal(0, _host.Subscribers);
    }

    [Fact]
    public async Task A_page_that_finishes_loading_after_cancel_is_not_read()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        vm.CancelCommand.Execute(null);

        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal(0, _host.CaptureCount);
        Assert.Null(_store.Latest);
        Assert.Equal(SignInOutcome.Cancelled, vm.Result!.Outcome);
    }

    [Fact]
    public async Task Cancelling_during_a_read_cancels_the_capture_and_changes_nothing()
    {
        var vm = Build();
        CancellationToken seen = default;
        _host.OnCaptureAsync = ct =>
        {
            seen = ct;
            return new TaskCompletionSource<PageSnapshot>().Task;
        };
        var running = vm.HandleNavigationAsync(AqwUrls.Home);

        vm.CancelCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(seen.IsCancellationRequested);
        Assert.Equal(SignInResult.Cancelled(), vm.Result);
        Assert.Null(vm.CharacterName);
        Assert.Null(_store.Latest);
        Assert.Null(_failures.Latest);
    }

    [Fact]
    public void A_browser_that_could_not_start_shows_the_reason_without_offering_a_retry()
    {
        var vm = Build();

        vm.ReportStartFailure("WebView2 runtime missing");

        Assert.Equal(SignInState.Error, vm.State);
        Assert.Contains("WebView2 runtime missing", vm.Message, StringComparison.Ordinal);
        Assert.False(vm.CanTryAgain);
        vm.TryAgainCommand.Execute(null);
        Assert.Equal(1, _host.NavigateCount);
        vm.CancelCommand.Execute(null);
        Assert.Equal(SignInOutcome.Failed, vm.Result!.Outcome);
    }

    [Fact]
    public void Try_again_does_nothing_unless_there_is_an_error()
    {
        var vm = Build();

        vm.TryAgainCommand.Execute(null);

        Assert.Equal(1, _host.NavigateCount);
    }

    [Fact]
    public void Disposing_stops_listening_to_the_browser()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");

        vm.Dispose();
        vm.Dispose();
        _host.Raise(AqwUrls.Home);

        Assert.Equal(0, _host.CaptureCount);
        Assert.Equal(0, _host.Subscribers);
    }

    [Fact]
    public void Visibility_flags_follow_mode_and_state()
    {
        var vm = Build(SignInMode.Compact);
        Assert.True(vm.ShowBrowser);
        Assert.True(vm.ShowCompactProgress);
        Assert.False(vm.ShowFullStatus);
        Assert.False(vm.ShowFullSuccess);

        vm.Mode = SignInMode.Full;
        Assert.False(vm.ShowCompactProgress);
        Assert.True(vm.ShowFullStatus);
        Assert.Equal(SignInViewModel.FullTitle, vm.WindowTitle);
    }

    // ---- Navigation watchdog -------------------------------------------------------------------------

    private static void AssertCouldNotReach(SignInViewModel vm)
    {
        Assert.Equal(SignInState.Error, vm.State);
        Assert.Equal(SignInViewModel.CouldNotReachMessage, vm.Message);
        Assert.Equal("Could not reach account.aq.com. Check your connection and try again.", vm.Message);
        Assert.True(vm.IsFull);
        Assert.True(vm.CanTryAgain);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task Full_mode_without_any_navigation_fails_when_the_watchdog_expires()
    {
        var vm = Build(navigationTimeout: Fast);

        await vm.NavigationWatchdog!.WaitAsync(TimeSpan.FromSeconds(10));

        AssertCouldNotReach(vm);
    }

    [Fact]
    public async Task Compact_mode_without_any_navigation_fails_into_the_full_layout_when_the_watchdog_expires()
    {
        var vm = Build(SignInMode.Compact, navigationTimeout: Fast);

        await vm.NavigationWatchdog!.WaitAsync(TimeSpan.FromSeconds(10));

        AssertCouldNotReach(vm);
        Assert.False(vm.IsCompact);
    }

    [Fact]
    public void The_watchdog_default_is_twenty_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(20), SignInViewModel.DefaultNavigationTimeout);

    [Fact]
    public async Task Any_completed_navigation_in_full_mode_cancels_the_watchdog_even_on_another_domain()
    {
        var vm = Build(navigationTimeout: Slow);
        var watchdog = vm.NavigationWatchdog!;

        _host.Raise("https://sso.example/login");

        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(SignInState.SigningIn, vm.State);
        Assert.Equal(SignInViewModel.SigningInMessage, vm.Message);
        Assert.Null(vm.NavigationWatchdog);
    }

    [Fact]
    public async Task A_full_mode_watchdog_that_was_cancelled_by_a_navigation_never_fails_later()
    {
        var vm = Build(navigationTimeout: Fast);
        var watchdog = vm.NavigationWatchdog!;
        _host.Raise("https://sso.example/login");
        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));

        await Task.Delay(Fast * 5);

        Assert.Equal(SignInState.SigningIn, vm.State);
    }

    [Fact]
    public async Task A_foreign_first_navigation_in_compact_mode_switches_to_full_and_cancels_the_watchdog()
    {
        var vm = Build(SignInMode.Compact, navigationTimeout: Slow);
        var watchdog = vm.NavigationWatchdog!;

        _host.Raise("https://sso.example/login");

        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.IsFull);
        Assert.Equal(SignInState.SessionExpired, vm.State);
        Assert.Equal(SignInViewModel.SessionExpiredMessage, vm.Message);
        Assert.Equal(0, _host.CaptureCount);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task An_account_navigation_in_compact_mode_cancels_the_watchdog()
    {
        var vm = Build(SignInMode.Compact, navigationTimeout: Slow);
        var watchdog = vm.NavigationWatchdog!;
        _host.OnCapture = () => Page("account-home-sample.html");

        await vm.HandleNavigationAsync(AqwUrls.Home);

        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
    }

    [Fact]
    public async Task A_failed_navigation_before_any_page_loaded_fails_at_once()
    {
        var vm = Build(navigationTimeout: Slow);
        var watchdog = vm.NavigationWatchdog!;

        _host.RaiseFailure();

        AssertCouldNotReach(vm);
        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_failed_navigation_before_any_page_loaded_fails_a_compact_refresh_too()
    {
        var vm = Build(SignInMode.Compact, navigationTimeout: Slow);

        _host.RaiseFailure();

        AssertCouldNotReach(vm);
    }

    [Fact]
    public void A_failed_navigation_after_a_page_loaded_does_not_interrupt_the_user()
    {
        var vm = Build(navigationTimeout: Slow);
        _host.Raise("https://sso.example/login");

        _host.RaiseFailure();

        Assert.Equal(SignInState.SigningIn, vm.State);
        Assert.Equal(SignInViewModel.SigningInMessage, vm.Message);
    }

    [Fact]
    public async Task Try_again_after_a_watchdog_failure_arms_the_watchdog_again()
    {
        var vm = Build(navigationTimeout: Fast);
        await vm.NavigationWatchdog!.WaitAsync(TimeSpan.FromSeconds(10));

        vm.TryAgainCommand.Execute(null);

        Assert.Equal(2, _host.NavigateCount);
        Assert.Equal(SignInState.SigningIn, vm.State);
        await vm.NavigationWatchdog!.WaitAsync(TimeSpan.FromSeconds(10));
        AssertCouldNotReach(vm);
    }

    [Fact]
    public void Try_again_after_a_watchdog_failure_does_not_arm_it_once_a_page_has_loaded()
    {
        var vm = Build(navigationTimeout: Slow);
        _host.RaiseFailure();
        _host.Raise("https://sso.example/login");

        vm.TryAgainCommand.Execute(null);

        Assert.Null(vm.NavigationWatchdog);
    }

    [Fact]
    public void A_page_that_arrives_after_the_watchdog_gave_up_clears_the_could_not_reach_message()
    {
        var vm = Build(navigationTimeout: Slow);
        _host.RaiseFailure();

        _host.Raise("https://sso.example/login");

        Assert.Equal(SignInState.SigningIn, vm.State);
        Assert.Equal(SignInViewModel.SigningInMessage, vm.Message);
    }

    [Fact]
    public async Task The_watchdog_does_not_fire_after_the_window_was_cancelled()
    {
        var vm = Build(navigationTimeout: Fast);
        var watchdog = vm.NavigationWatchdog!;

        vm.CancelCommand.Execute(null);

        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(Fast * 5);
        Assert.Equal(SignInState.Cancelled, vm.State);
        Assert.Equal(SignInResult.Cancelled(), vm.Result);
        Assert.Null(vm.NavigationWatchdog);
    }

    [Fact]
    public async Task The_watchdog_does_not_fire_after_dispose()
    {
        var vm = Build(navigationTimeout: Fast);
        var watchdog = vm.NavigationWatchdog!;

        vm.Dispose();

        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(Fast * 5);
        Assert.Equal(SignInState.Cancelled, vm.State);
        Assert.Null(vm.NavigationWatchdog);
    }

    [Fact]
    public void Finishing_unsubscribes_from_navigation_failures_too()
    {
        var vm = Build();
        Assert.Equal(1, _host.FailureSubscribers);

        vm.CancelCommand.Execute(null);

        Assert.Equal(0, _host.FailureSubscribers);
        _host.RaiseFailure();
        Assert.Equal(SignInState.Cancelled, vm.State);
    }

    // ---- Shutdown safety and cancelling during ingestion ---------------------------------------------

    private bool LoggedTimeout() =>
        _logs.Events.Any(e => e.Level >= LogEventLevel.Warning
            && e.MessageTemplate.Text.Contains("timed out", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Dispose_finishes_as_cancelled_and_closes_once()
    {
        var vm = Build();
        var closed = new List<SignInResult>();
        vm.CloseRequested += (_, r) => closed.Add(r);

        vm.Dispose();
        vm.Dispose();

        Assert.Equal(SignInResult.Cancelled(), vm.Result);
        Assert.Equal(SignInResult.Cancelled(), Assert.Single(closed));
    }

    [Fact]
    public async Task Disposing_during_a_read_ends_cancelled_without_an_exception_or_a_timeout_warning()
    {
        var vm = Build(readTimeout: TimeSpan.FromMinutes(5));
        _host.OnCaptureAsync = _ => new TaskCompletionSource<PageSnapshot>().Task;
        var running = vm.HandleNavigationAsync(AqwUrls.Home);

        vm.Dispose();
        var error = await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Null(error);
        Assert.Equal(SignInResult.Cancelled(), vm.Result);
        Assert.False(LoggedTimeout());
        Assert.Null(_store.Latest);
    }

    [Fact]
    public async Task Cancelling_during_a_read_logs_no_timeout_warning()
    {
        var vm = Build(readTimeout: TimeSpan.FromMinutes(5));
        _host.OnCaptureAsync = _ => new TaskCompletionSource<PageSnapshot>().Task;
        var running = vm.HandleNavigationAsync(AqwUrls.Home);

        vm.CancelCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(LoggedTimeout());
    }

    [Fact]
    public async Task A_genuine_read_timeout_is_still_logged()
    {
        var vm = Build(readTimeout: TimeSpan.FromMilliseconds(30));
        _host.OnCaptureAsync = _ => new TaskCompletionSource<PageSnapshot>().Task;

        await vm.HandleNavigationAsync(AqwUrls.Home).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(LoggedTimeout());
    }

    [Fact]
    public async Task Disposing_during_the_connected_pause_reports_the_name_without_an_exception()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        _delay.Gate = new TaskCompletionSource();
        var running = vm.HandleNavigationAsync(AqwUrls.Home);

        vm.Dispose();
        var error = await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Null(error);
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
        Assert.False(LoggedTimeout());
    }

    [Fact]
    public async Task A_read_that_ends_after_dispose_changes_nothing_and_does_not_throw()
    {
        var vm = Build();
        var gate = new TaskCompletionSource();
        _host.OnCaptureAsync = async _ =>
        {
            await gate.Task;
            return Page("account-home-sample.html");
        };
        var running = vm.HandleNavigationAsync(AqwUrls.Home);
        vm.Dispose();
        gate.SetResult();

        var error = await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Null(error);
        Assert.Equal(SignInResult.Cancelled(), vm.Result);
        Assert.Null(_store.Latest);
        Assert.Null(_failures.Latest);
    }

    [Fact]
    public async Task Navigation_after_dispose_is_ignored_without_touching_disposed_state()
    {
        var vm = Build();
        vm.Dispose();
        _host.OnCapture = () => Page("account-home-sample.html");

        var error = await Record.ExceptionAsync(() => vm.HandleNavigationAsync(AqwUrls.Home));
        vm.TryAgainCommand.Execute(null);
        vm.CancelCommand.Execute(null);
        vm.ReportStartFailure("late");

        Assert.Null(error);
        Assert.Equal(0, _host.CaptureCount);
        Assert.Equal(SignInResult.Cancelled(), vm.Result);
    }

    [Fact]
    public async Task Cancelling_after_the_capture_finished_does_not_persist_or_buffer_anything()
    {
        var vm = Build();
        _host.OnCaptureAsync = _ =>
        {
            // The capture completes, but the user cancels before the result is ingested.
            vm.CancelCommand.Execute(null);
            return Task.FromResult(Page("account-home-sample.html"));
        };

        await vm.HandleNavigationAsync(AqwUrls.Home).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(_store.Latest);
        Assert.Null(_failures.Latest);
        Assert.Null(vm.CharacterName);
        Assert.Equal(SignInResult.Cancelled(), vm.Result);
    }

    [Fact]
    public async Task Cancelling_after_a_drifted_capture_does_not_buffer_the_snapshot()
    {
        var vm = Build();
        _host.OnCaptureAsync = _ =>
        {
            vm.CancelCommand.Execute(null);
            return Task.FromResult(Page("account-home-drifted.html"));
        };

        await vm.HandleNavigationAsync(AqwUrls.Home).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(_failures.Latest);
        Assert.Equal(SignInResult.Cancelled(), vm.Result);
    }
}
