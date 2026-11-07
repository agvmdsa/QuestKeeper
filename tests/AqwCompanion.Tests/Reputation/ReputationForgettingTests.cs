using System.Text;
using AqwCompanion.App.Reputation;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Reputation;

public class ReputationForgettingTests
{
    private const string OneFaction = """{"schema":1,"factions":[{"factionId":1,"name":"Good","rank":5,"rep":10,"repToRank":20}]}""";

    private static readonly DateTimeOffset At = new(2030, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public ReputationStore Store { get; } = new(new InMemoryStoredDataStore());

        public FakeReputationFilePicker Picker { get; } = new();

        public FakeReputationAutoSource Auto { get; } = new();

        public ReputationViewModel Vm { get; }

        public Fixture() => Vm = new ReputationViewModel(Store, Picker, Auto, new FixedTime(At));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void The_name_is_Reputation()
    {
        Assert.Equal("Reputation", new Fixture().Vm.Name);
    }

    [Fact]
    public async Task Quiesce_makes_the_answer_of_a_pending_refresh_ignored()
    {
        var f = new Fixture();
        var release = new TaskCompletionSource();
        f.Auto.OnOpen = () =>
        {
            release.Task.Wait();
            var bytes = Encoding.UTF8.GetBytes(OneFaction);
            return new ReputationAutoRead(ReputationAutoStatus.Found, new ReputationAutoFile(new MemoryStream(bytes), bytes.Length, At));
        };
        var refresh = f.Vm.RefreshCommand.ExecuteAsync(null);

        f.Vm.Quiesce();
        release.SetResult();
        await refresh;

        Assert.True(f.Vm.IsEmpty);
        Assert.Null(f.Store.Load());
    }

    [Fact]
    public async Task A_quiesced_view_model_ignores_refresh_and_import()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction);
        f.Picker.Returns(OneFaction);
        f.Vm.Quiesce();

        await f.Vm.RefreshCommand.ExecuteAsync(null);
        await f.Vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(0, f.Auto.OpenCalls);
        Assert.True(f.Vm.IsEmpty);
        Assert.Null(f.Store.Load());
    }

    [Fact]
    public async Task Resume_lets_a_refresh_work_again()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction);
        f.Vm.Quiesce();
        f.Vm.Resume();

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(f.Vm.Items);
    }

    [Fact]
    public async Task Forget_clears_the_items_and_deletes_the_side_file()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction);
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        f.Vm.Quiesce();

        f.Vm.Forget();

        Assert.Empty(f.Vm.Items);
        Assert.True(f.Vm.IsEmpty);
        Assert.Equal(1, f.Auto.DeleteCalls);
    }

    [Fact]
    public async Task Forget_also_lets_the_view_model_work_again()
    {
        var f = new Fixture();
        f.Auto.Returns(OneFaction);
        f.Vm.Quiesce();
        f.Vm.Forget();

        await f.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(f.Vm.Items);
    }
}
