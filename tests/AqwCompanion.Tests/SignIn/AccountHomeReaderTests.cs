using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;
using Serilog;

namespace AqwCompanion.Tests.SignIn;

public class AccountHomeReaderTests
{
    private static readonly TimeSpan Slow = TimeSpan.FromMinutes(5);

    private readonly FakeBrowserHost _host = new();
    private readonly InMemoryStore<AccountIdentity> _store = new();
    private readonly FailedSnapshotBuffer _failures = new();
    private Func<PageSnapshot, ParseResult<AccountIdentity>> _parse = _ => Ok("Hero");

    private static ParseResult<AccountIdentity> Ok(string name, AccountSummary? summary = null) =>
        ParseResult<AccountIdentity>.Ok(
            new AccountIdentity(name, summary), ParseDiagnostics.Empty("fake", "1"));

    private static PageSnapshot Snapshot() =>
        PageSnapshot.Create("https://account.aq.com/Home", "<html></html>", "1.0.0", DateTimeOffset.UnixEpoch);

    private AccountHomeReader Build(TimeSpan? readTimeout = null)
    {
        _host.OnCapture = Snapshot;
        var ingestion = new IngestionService<AccountIdentity>(
            new FakeParser<AccountIdentity>(s => _parse(s)),
            _store,
            _failures,
            new ListLogger<IngestionService<AccountIdentity>>());
        return new AccountHomeReader(
            _host, ingestion, readTimeout ?? Slow, new LoggerConfiguration().CreateLogger());
    }

    [Fact]
    public async Task An_ok_parse_returns_the_name_and_summary()
    {
        var summary = new AccountSummary(5, "Gold", "Member", 2);
        _parse = _ => Ok("Hero", summary);

        var read = await Build().ReadAsync(CancellationToken.None, () => false);

        Assert.Equal("Hero", read.Name);
        Assert.Equal(summary, read.Summary);
        Assert.Equal(AccountHomeReadFailure.None, read.Failure);
        Assert.Single(_store.Saved);
    }

    [Fact]
    public async Task A_failed_parse_is_not_recognized()
    {
        _parse = _ => ParseResult<AccountIdentity>.Failed("nope", ParseDiagnostics.Empty("fake", "1"));

        var read = await Build().ReadAsync(CancellationToken.None, () => false);

        Assert.Null(read.Name);
        Assert.Equal(AccountHomeReadFailure.NotRecognized, read.Failure);
    }

    [Fact]
    public async Task A_suspicious_parse_is_not_recognized()
    {
        _parse = _ => ParseResult<AccountIdentity>.Suspicious(
            new AccountIdentity("Hero"), "odd", ParseDiagnostics.Empty("fake", "1"));

        var read = await Build().ReadAsync(CancellationToken.None, () => false);

        Assert.Null(read.Name);
        Assert.Equal(AccountHomeReadFailure.NotRecognized, read.Failure);
    }

    [Fact]
    public async Task A_capture_slower_than_the_timeout_times_out()
    {
        var reader = Build(TimeSpan.FromMilliseconds(20));
        _host.OnCaptureAsync = async ct =>
        {
            await Task.Delay(Slow, ct);
            return Snapshot();
        };

        var read = await reader.ReadAsync(CancellationToken.None, () => false);

        Assert.Equal(AccountHomeReadFailure.TimedOut, read.Failure);
        Assert.Null(read.Name);
    }

    [Fact]
    public async Task A_capture_that_ignores_cancellation_still_times_out()
    {
        var reader = Build(TimeSpan.FromMilliseconds(20));
        var never = new TaskCompletionSource<PageSnapshot>();
        _host.OnCaptureAsync = _ => never.Task;

        var read = await reader.ReadAsync(CancellationToken.None, () => false);

        Assert.Equal(AccountHomeReadFailure.TimedOut, read.Failure);
    }

    [Fact]
    public async Task A_capture_that_throws_reports_its_message()
    {
        var reader = Build();
        _host.OnCaptureAsync = _ => throw new InvalidOperationException("boom");

        var read = await reader.ReadAsync(CancellationToken.None, () => false);

        Assert.Equal(AccountHomeReadFailure.CaptureFailed, read.Failure);
        Assert.Equal("boom", read.CaptureError);
        Assert.Null(read.Name);
    }

    [Fact]
    public async Task Stopped_before_the_capture_is_dismissed_without_capturing()
    {
        var read = await Build().ReadAsync(CancellationToken.None, () => true);

        Assert.Equal(AccountHomeReadFailure.Dismissed, read.Failure);
        Assert.Equal(0, _host.CaptureCount);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task Stopped_after_the_capture_is_dismissed_and_nothing_is_ingested()
    {
        var reader = Build();
        var stopped = false;
        _host.OnCaptureAsync = _ =>
        {
            stopped = true;
            return Task.FromResult(Snapshot());
        };

        var read = await reader.ReadAsync(CancellationToken.None, () => stopped);

        Assert.Equal(AccountHomeReadFailure.Dismissed, read.Failure);
        Assert.Null(read.Name);
        Assert.Empty(_store.Saved);
        Assert.Null(_failures.Latest);
    }

    [Fact]
    public async Task Stopped_after_the_ingestion_is_dismissed()
    {
        var reader = Build();
        var stopped = false;
        _parse = _ =>
        {
            stopped = true;
            return Ok("Hero");
        };

        var read = await reader.ReadAsync(CancellationToken.None, () => stopped);

        Assert.Equal(AccountHomeReadFailure.Dismissed, read.Failure);
        Assert.Null(read.Name);
    }

    [Fact]
    public async Task A_cancelled_lifetime_is_dismissed_not_timed_out()
    {
        var reader = Build();
        using var lifetime = new CancellationTokenSource();
        _host.OnCaptureAsync = async ct =>
        {
            await lifetime.CancelAsync();
            await Task.Delay(Slow, ct);
            return Snapshot();
        };

        var read = await reader.ReadAsync(lifetime.Token, () => false);

        Assert.Equal(AccountHomeReadFailure.Dismissed, read.Failure);
    }
}
