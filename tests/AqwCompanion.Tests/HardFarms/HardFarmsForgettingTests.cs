using AqwCompanion.App.Account;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmsForgettingTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly HardFarmStore _store = new(new InMemoryStoredDataStore());
    private readonly FakeHardFarmSync _sync = new() { Result = new SyncResult(SyncStatus.Updated, "Stored.", []) };
    private readonly HardFarmsViewModel _vm;

    public HardFarmsForgettingTests() =>
        _vm = new HardFarmsViewModel(_store, _sync, new InventoryCardViewModel(new SidebarViewModel()), new FakeUrlLauncher());

    [Fact]
    public void The_name_is_Hard_Farms()
    {
        Assert.Equal("Hard Farms", _vm.Name);
    }

    [Fact]
    public async Task Quiesce_cancels_a_running_update_and_returns_to_idle()
    {
        _sync.Gate = new TaskCompletionSource();
        var run = _vm.UpdateCommand.ExecuteAsync(null);
        Assert.True(_vm.IsUpdating);

        _vm.Quiesce();

        Assert.True(_sync.Token.IsCancellationRequested);
        Assert.Equal(HardFarmsState.Idle, _vm.State);
        _sync.Gate.SetResult();
        await run;
        Assert.Equal(HardFarmsState.Idle, _vm.State);
    }

    [Fact]
    public async Task A_quiesced_view_model_ignores_the_update_command()
    {
        _vm.Quiesce();

        Assert.False(_vm.UpdateCommand.CanExecute(null));
        await _vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, _sync.Calls);
    }

    [Fact]
    public async Task Resume_enables_the_update_command_again()
    {
        _vm.Quiesce();
        _vm.Resume();

        Assert.True(_vm.UpdateCommand.CanExecute(null));
        await _vm.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, _sync.Calls);
    }

    [Fact]
    public async Task Forget_empties_the_list_and_enables_the_update_command()
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), At);
        await _vm.RestoreAsync();
        Assert.Single(_vm.Rows);
        _vm.SearchText = "blade";
        _vm.Quiesce();

        _vm.Forget();

        Assert.Empty(_vm.Rows);
        Assert.True(_vm.ShowEmptyState);
        Assert.Equal(string.Empty, _vm.SearchText);
        Assert.True(_vm.UpdateCommand.CanExecute(null));
    }
}
