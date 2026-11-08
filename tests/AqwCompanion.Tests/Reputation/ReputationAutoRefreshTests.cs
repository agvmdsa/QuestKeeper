using System.Text;
using AqwCompanion.App.Reputation;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Reputation;

public class ReputationAutoRefreshTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Older = new(2030, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Newer = new(2030, 3, 1, 8, 20, 0, TimeSpan.Zero);

    private const string OneFaction =
        """{"schema":1,"exportedAt":"2030-03-01T08:00:00Z","factions":[{"factionId":1,"name":"Good","rank":5,"rep":10,"repToRank":20}]}""";

    private const string TwoFactions =
        """{"schema":1,"factions":[{"factionId":2,"name":"evil","rank":1,"rep":1500,"repToRank":2000},{"factionId":1,"name":"Good","rank":5,"rep":10,"repToRank":20}]}""";

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Fixture
    {
        public ReputationStore Store { get; }

        public FakeReputationAutoSource Auto { get; } = new();

        public ReputationViewModel Vm { get; }

        public Fixture()
        {
            Store = new ReputationStore(new InMemoryStoredDataStore().Module());
            Vm = new ReputationViewModel(Store, new FakeReputationFilePicker(), Auto, new FixedClock(At));
        }
    }

    [Fact]
    public async Task A_missing_file_keeps_the_data_and_names_the_expected_path_without_an_error()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        f.Auto.Missing();

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal($"No file from the game client yet. Expected at {f.Auto.Path}.", f.Vm.StatusText);
        Assert.False(f.Vm.HasError);
        Assert.Single(f.Vm.Items);
        Assert.StartsWith("Checked ", f.Vm.CheckedLine);
        Assert.Equal(f.Auto.Path, f.Vm.ExpectedPath);
    }

    [Fact]
    public async Task A_new_file_replaces_the_data_and_is_saved_with_the_export_time()
    {
        var f = new Fixture();
        f.Auto.Returns(TwoFactions, Newer);

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["evil", "Good"], f.Vm.Items.Select(r => r.Name));
        Assert.Equal("2 factions loaded from the game client.", f.Vm.StatusText);
        Assert.EndsWith(" from reputation.json (auto)", f.Vm.ImportedLine);
        var stored = f.Store.Load()!;
        Assert.Equal("reputation.json (auto)", stored.SourceName);
        Assert.Equal(2, stored.Snapshot.Factions.Count);
    }

    [Fact]
    public async Task The_export_time_of_the_file_is_kept()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction);

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("2030-03-01T08:00:00Z", f.Store.Load()!.Snapshot.ExportedAt);
    }

    [Fact]
    public async Task An_unchanged_file_is_not_read_again_and_only_the_checked_line_is_updated()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction, Older);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        var items = f.Vm.Items;
        f.Vm.StatusText = "kept";
        f.Auto.Returns(TwoFactions, Older, length: Encoding.UTF8.GetByteCount(OneFaction));

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Same(items, f.Vm.Items);
        Assert.Equal("kept", f.Vm.StatusText);
        Assert.StartsWith("Checked ", f.Vm.CheckedLine);
        Assert.Single(f.Store.Load()!.Snapshot.Factions);
    }

    [Fact]
    public async Task A_file_with_another_write_time_replaces_the_data()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction, Older);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        f.Auto.Returns(TwoFactions, Newer);

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, f.Vm.Items.Count);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"schema":2,"factions":[]}""")]
    public async Task An_invalid_file_shows_an_error_and_keeps_the_previous_data(string content)
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction, Older);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        var items = f.Vm.Items;
        f.Auto.Returns(content, Newer);

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.EndsWith(ReputationViewModel.NothingChangedSuffix, f.Vm.ErrorText);
        Assert.Same(items, f.Vm.Items);
        Assert.Single(f.Store.Load()!.Snapshot.Factions);
    }

    [Fact]
    public async Task A_file_that_is_too_large_is_rejected_without_being_read()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction, Newer, length: ReputationParser.MaxBytes + 1);

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("too large", f.Vm.ErrorText);
        Assert.True(f.Vm.IsEmpty);
        Assert.Null(f.Store.Load());
    }

    [Fact]
    public async Task A_file_that_cannot_be_opened_shows_an_error_and_is_tried_again_next_time()
    {
        var f = new Fixture();
        f.Auto.OnOpen = () => new ReputationAutoRead(ReputationAutoStatus.Unreadable);

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ReputationViewModel.ReadFailedMessage, f.Vm.ErrorText);
        f.Auto.Returns(OneFaction);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, f.Vm.ErrorText);
        Assert.Single(f.Vm.Items);
    }

    [Fact]
    public async Task A_refresh_during_a_refresh_is_ignored()
    {
        var f = new Fixture();
        var release = new TaskCompletionSource();
        var opens = 0;
        f.Auto.OnOpen = () =>
        {
            Interlocked.Increment(ref opens);
            release.Task.Wait();
            return new ReputationAutoRead(ReputationAutoStatus.Missing);
        };

        var first = f.Vm.RefreshCommand.ExecuteAsync(null);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        release.SetResult();
        await first;

        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task Forget_deletes_the_file_and_forgets_the_last_read_so_the_same_file_is_read_again()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction, Older);
        await f.Vm.RefreshCommand.ExecuteAsync(null);

        f.Vm.Quiesce();
        f.Vm.Forget();

        Assert.Equal(1, f.Auto.DeleteCalls);
        Assert.True(f.Vm.IsEmpty);
        Assert.Equal(string.Empty, f.Vm.CheckedLine);
        f.Auto.Returns(OneFaction, Older);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.Single(f.Vm.Items);
    }

    [Fact]
    public async Task A_refresh_that_started_before_a_reset_is_ignored()
    {
        var f = new Fixture();
        var release = new TaskCompletionSource();
        f.Auto.OnOpen = () =>
        {
            release.Task.Wait();
            var bytes = Encoding.UTF8.GetBytes(OneFaction);
            return new ReputationAutoRead(ReputationAutoStatus.Found, new ReputationAutoFile(new MemoryStream(bytes), bytes.Length, Older));
        };
        var refresh = f.Vm.RefreshCommand.ExecuteAsync(null);

        f.Vm.Quiesce();
        f.Vm.Forget();
        release.SetResult();
        await refresh;

        Assert.True(f.Vm.IsEmpty);
        Assert.Null(f.Store.Load());
    }
}
