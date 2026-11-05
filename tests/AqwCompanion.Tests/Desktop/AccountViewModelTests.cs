using AqwCompanion.App;
using AqwCompanion.App.ViewModels;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Desktop;

public class AccountViewModelTests
{
    private const string LoginUrl = "https://account.aq.com/Login";

    private readonly FakeBrowserHost _host = new();
    private readonly FailedSnapshotBuffer _failures = new();
    private readonly LatestResultStore<AccountIdentity> _store = new();

    private AccountViewModel Build(TimeSpan? readTimeout = null) =>
        new(_host, new IngestionService<AccountIdentity>(
            new AccountHomeParser(), _store, _failures, new ListLogger<IngestionService<AccountIdentity>>()),
            readTimeout);

    private static PageSnapshot Page(string fixture, string url = AqwUrls.Home) =>
        PageSnapshot.Create(
            url,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)),
            "1.0.0",
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void Starts_closed_without_a_character()
    {
        var vm = Build();

        Assert.False(vm.IsBrowserOpen);
        Assert.Null(vm.CharacterName);
        Assert.False(vm.HasCharacter);
        Assert.Null(_host.LastNavigated);
        Assert.False(string.IsNullOrWhiteSpace(vm.Status));
    }

    [Fact]
    public void Sign_in_opens_the_browser_at_account_home()
    {
        var vm = Build();

        vm.SignInCommand.Execute(null);

        Assert.True(vm.IsBrowserOpen);
        Assert.Equal(AqwUrls.Home, _host.LastNavigated);
        Assert.Equal(AccountViewModel.WaitingMessage, vm.Status);
    }

    [Fact]
    public void Open_home_navigates_to_account_home()
    {
        var vm = Build();
        _host.Navigate(LoginUrl);

        vm.OpenHomeCommand.Execute(null);

        Assert.Equal(AqwUrls.Home, _host.LastNavigated);
    }

    [Fact]
    public async Task Navigation_to_a_non_home_url_does_nothing_but_wait()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-login.html", LoginUrl);

        await vm.HandleNavigationAsync(LoginUrl);

        Assert.Equal(0, _host.CaptureCount);
        Assert.Null(vm.CharacterName);
        Assert.Equal(AccountViewModel.WaitingMessage, vm.Status);
        Assert.Null(_failures.Latest);
    }

    [Fact]
    public void Navigation_completed_on_home_captures_and_shows_the_name()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");

        _host.Raise(AqwUrls.Home);

        Assert.Equal(1, _host.CaptureCount);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.True(vm.HasCharacter);
        Assert.Equal("Character: Test Hero", vm.CharacterText);
        Assert.Equal(new AccountIdentity("Test Hero"), _store.Latest);
        Assert.Null(_failures.Latest);
    }

    [Fact]
    public async Task Failed_parse_shows_the_help_message_buffers_the_snapshot_and_keeps_no_name()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        await vm.HandleNavigationAsync(AqwUrls.Home);
        _host.OnCapture = () => Page("account-home-drifted.html");

        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Null(vm.CharacterName);
        Assert.False(vm.HasCharacter);
        Assert.Equal(AccountViewModel.ReadFailedMessage, vm.Status);
        Assert.NotNull(_failures.Latest);
    }

    [Fact]
    public async Task Capture_exception_is_reported_in_the_status_without_crashing()
    {
        var vm = Build();
        _host.OnCapture = () => throw new InvalidOperationException("script failed");

        var error = await Record.ExceptionAsync(() => vm.HandleNavigationAsync(AqwUrls.Home));

        Assert.Null(error);
        Assert.Null(vm.CharacterName);
        Assert.StartsWith("Could not capture the page", vm.Status, StringComparison.Ordinal);
        Assert.Contains("script failed", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_exception_raised_through_the_event_does_not_escape()
    {
        var vm = Build();

        var error = Record.Exception(() => _host.Raise(AqwUrls.Home));

        Assert.Null(error);
        Assert.StartsWith("Could not capture the page", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_failed_message_is_honest_about_the_diagnostics_steps()
    {
        var message = AccountViewModel.ReadFailedMessage;

        Assert.DoesNotContain("redacted snapshot", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Diagnostics", message, StringComparison.Ordinal);
        Assert.Contains("Include the raw page snapshot of the last failed capture", message, StringComparison.Ordinal);
        Assert.Contains("best-effort", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_that_never_completes_times_out_and_a_later_home_event_works()
    {
        var vm = Build(TimeSpan.FromMilliseconds(50));
        _host.OnCaptureAsync = _ => new TaskCompletionSource<PageSnapshot>().Task;

        await vm.HandleNavigationAsync(AqwUrls.Home).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(AccountViewModel.ReadTimedOutMessage, vm.Status);
        Assert.Null(vm.CharacterName);

        _host.OnCaptureAsync = null;
        _host.OnCapture = () => Page("account-home-sample.html");
        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal(2, _host.CaptureCount);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal(AccountViewModel.ReadMessage, vm.Status);
    }

    [Fact]
    public async Task The_capture_is_given_a_token_that_is_cancelled_on_timeout()
    {
        var vm = Build(TimeSpan.FromMilliseconds(50));
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
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal(AccountViewModel.ReadMessage, vm.Status);
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
        Assert.Equal("Test Hero", vm.CharacterName);
    }

    [Fact]
    public async Task Leaving_home_keeps_the_name_but_labels_it_as_the_last_read()
    {
        var vm = Build();
        _host.OnCapture = () => Page("account-home-sample.html");
        await vm.HandleNavigationAsync(AqwUrls.Home);

        await vm.HandleNavigationAsync(LoginUrl);

        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.True(vm.HasCharacter);
        Assert.Equal("Last read: Test Hero", vm.CharacterText);
        Assert.Equal(AccountViewModel.WaitingMessage, vm.Status);

        await vm.HandleNavigationAsync(AqwUrls.Home);

        Assert.Equal("Character: Test Hero", vm.CharacterText);
    }
}
