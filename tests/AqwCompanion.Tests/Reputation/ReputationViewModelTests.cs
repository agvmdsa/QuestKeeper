using AqwCompanion.App.Reputation;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Reputation;

public class ReputationViewModelTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private const string TwoFactions =
        """{"schema":1,"factions":[{"factionId":2,"name":"evil","rank":1,"rep":1500,"repToRank":2000,"spillRep":500},{"factionId":1,"name":"Good","rank":5,"rep":10,"repToRank":20}]}""";

    private sealed class Fixture
    {
        public InMemoryStoredDataStore Inner { get; } = new();

        public ReputationStore Store { get; }

        public FakeReputationFilePicker Picker { get; } = new();

        public FakeReputationAutoSource Auto { get; } = new();

        public ReputationViewModel Vm { get; }

        public Fixture()
        {
            Store = new ReputationStore(Inner);
            Vm = new ReputationViewModel(Store, Picker, Auto, new FixedClock(At));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void A_new_view_model_is_empty()
    {
        var f = new Fixture();

        Assert.True(f.Vm.IsEmpty);
        Assert.False(f.Vm.HasData);
        Assert.Empty(f.Vm.Items);
        Assert.Equal(string.Empty, f.Vm.ImportedLine);
        Assert.Equal(string.Empty, f.Vm.ErrorText);
    }

    [Fact]
    public async Task Restore_shows_the_stored_reputation()
    {
        var f = new Fixture();
        f.Store.Save(new StoredReputation(new ReputationSnapshot([new FactionStanding(1, "Good", 5, 1234, 5000, 0)], null), "old.json", At));

        await f.Vm.RestoreAsync();

        Assert.True(f.Vm.HasData);
        Assert.False(f.Vm.IsEmpty);
        var row = Assert.Single(f.Vm.Items);
        Assert.Equal(new ReputationRow("Good", "5", "1,234", "5,000"), row);
        Assert.StartsWith("Imported ", f.Vm.ImportedLine);
        Assert.EndsWith(" from old.json", f.Vm.ImportedLine);
    }

    [Fact]
    public async Task Restore_with_nothing_stored_stays_empty()
    {
        var f = new Fixture();

        await f.Vm.RestoreAsync();

        Assert.True(f.Vm.IsEmpty);
    }

    [Fact]
    public async Task A_successful_import_shows_the_sorted_list_saves_it_and_names_the_file()
    {
        var f = new Fixture();
        f.Picker.Returns(TwoFactions, "my-rep.json");

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(["evil", "Good"], f.Vm.Items.Select(r => r.Name));
        Assert.Equal("1,500", f.Vm.Items[0].Rep);
        Assert.Equal("1,500", f.Vm.Items[0].ToNextRank);
        Assert.Equal("20", f.Vm.Items[1].ToNextRank);
        Assert.True(f.Vm.HasData);
        Assert.Equal(string.Empty, f.Vm.ErrorText);
        Assert.Equal("2 factions imported.", f.Vm.StatusText);
        Assert.EndsWith(" from my-rep.json", f.Vm.ImportedLine);
        var stored = f.Store.Load()!;
        Assert.Equal("my-rep.json", stored.SourceName);
        Assert.Equal(At, stored.ImportedAt);
        Assert.Equal(2, stored.Snapshot.Factions.Count);
    }

    [Fact]
    public async Task The_status_reports_skipped_entries()
    {
        var f = new Fixture();
        f.Picker.Returns("""{"schema":1,"factions":[{"factionId":1,"name":"Good","rank":1,"rep":1,"repToRank":1},{"factionId":-1,"name":"Bad","rank":1,"rep":1,"repToRank":1}]}""");

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("1 factions imported, 1 entries skipped.", f.Vm.StatusText);
    }

    [Fact]
    public async Task Cancelling_the_picker_keeps_the_state_and_shows_no_error()
    {
        var f = new Fixture();
        f.Picker.Returns(TwoFactions);
        await f.Vm.ImportCommand.ExecuteAsync(null);
        var before = f.Vm.Items;
        var line = f.Vm.ImportedLine;
        f.Picker.OnPick = () => Task.FromResult<PickedFile?>(null);

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Same(before, f.Vm.Items);
        Assert.Equal(line, f.Vm.ImportedLine);
        Assert.Equal(string.Empty, f.Vm.ErrorText);
        Assert.Equal(2, f.Picker.PickCalls);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"schema":2,"factions":[]}""")]
    [InlineData("""{"schema":1,"factions":[]}""")]
    public async Task An_invalid_file_shows_an_error_and_keeps_the_previous_data(string content)
    {
        var f = new Fixture();
        f.Picker.Returns(TwoFactions, "good.json");
        await f.Vm.ImportCommand.ExecuteAsync(null);
        var before = f.Vm.Items;
        f.Picker.Returns(content, "bad.json");

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.NotEqual(string.Empty, f.Vm.ErrorText);
        Assert.True(f.Vm.HasError);
        Assert.EndsWith(ReputationViewModel.NothingChangedSuffix, f.Vm.ErrorText);
        Assert.Same(before, f.Vm.Items);
        Assert.EndsWith(" from good.json", f.Vm.ImportedLine);
        Assert.Equal("good.json", f.Store.Load()!.SourceName);
    }

    [Fact]
    public async Task An_oversize_file_is_rejected()
    {
        var f = new Fixture();
        f.Picker.OnPick = () => Task.FromResult<PickedFile?>(new PickedFile(new MemoryStream(new byte[ReputationParser.MaxBytes + 1]), "big.json"));

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("too large", f.Vm.ErrorText);
        Assert.True(f.Vm.IsEmpty);
        Assert.Null(f.Store.Load());
    }

    [Fact]
    public async Task A_picker_failure_shows_an_error_without_throwing()
    {
        var f = new Fixture();
        f.Picker.OnPick = () => throw new IOException("denied");

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(ReputationViewModel.OpenFailedMessage, f.Vm.ErrorText);
        Assert.True(f.Vm.IsEmpty);
    }

    [Fact]
    public async Task A_stream_that_fails_to_read_shows_an_error()
    {
        var f = new Fixture();
        f.Picker.OnPick = () => Task.FromResult<PickedFile?>(new PickedFile(new ThrowingStream(), "x.json"));

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(ReputationViewModel.ReadFailedMessage, f.Vm.ErrorText);
    }

    [Fact]
    public async Task A_later_success_clears_the_error()
    {
        var f = new Fixture();
        f.Picker.Returns("junk");
        await f.Vm.ImportCommand.ExecuteAsync(null);
        f.Picker.Returns(TwoFactions);

        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, f.Vm.ErrorText);
        Assert.False(f.Vm.HasError);
    }

    [Fact]
    public async Task Reset_returns_to_the_empty_state()
    {
        var f = new Fixture();
        f.Picker.Returns(TwoFactions);
        await f.Vm.ImportCommand.ExecuteAsync(null);

        f.Vm.Reset();

        Assert.True(f.Vm.IsEmpty);
        Assert.Empty(f.Vm.Items);
        Assert.Equal(string.Empty, f.Vm.ImportedLine);
        Assert.Equal(string.Empty, f.Vm.StatusText);
        Assert.Equal(string.Empty, f.Vm.ErrorText);
    }

    [Fact]
    public async Task An_import_that_was_picked_before_a_reset_is_ignored()
    {
        var f = new Fixture();
        var release = new TaskCompletionSource();
        f.Picker.OnPick = async () =>
        {
            await release.Task;
            return new PickedFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(TwoFactions)), "late.json");
        };
        var import = f.Vm.ImportCommand.ExecuteAsync(null);

        f.Vm.Reset();
        release.SetResult();
        await import;

        Assert.True(f.Vm.IsEmpty);
        Assert.Null(f.Store.Load());
    }

    [Fact]
    public async Task Restore_after_a_reset_does_not_bring_data_back_from_before()
    {
        var f = new Fixture();
        f.Store.Save(new StoredReputation(new ReputationSnapshot([new FactionStanding(1, "Good", 1, 1, 1, 0)], null), "a.json", At));
        var restore = f.Vm.RestoreAsync();

        f.Vm.Reset();
        await restore;

        Assert.True(f.Vm.IsEmpty);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("read failed");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
