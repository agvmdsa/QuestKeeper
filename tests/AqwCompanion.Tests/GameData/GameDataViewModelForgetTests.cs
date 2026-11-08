using AqwCompanion.App.GameData;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.GameData;

public class GameDataViewModelForgetTests
{
    private static ForgetCoordinator Coordinator(Func<bool> removeStoredData, params IForgettable[] participants) =>
        new(removeStoredData, () => participants);

    [Fact]
    public void Forgetting_is_not_offered_without_a_coordinator()
    {
        var vm = new CaptureRig().CreateViewModel();

        Assert.False(vm.RequestForgetCommand.CanExecute(null));
    }

    [Fact]
    public void Requesting_asks_for_confirmation_and_removes_nothing()
    {
        var removed = false;
        var vm = new CaptureRig().CreateViewModel(forgetting: Coordinator(() => removed = true));

        vm.RequestForgetCommand.Execute(null);

        Assert.True(vm.IsForgetPending);
        Assert.False(removed);
    }

    [Fact]
    public void Cancelling_keeps_the_data()
    {
        var removed = false;
        var vm = new CaptureRig().CreateViewModel(forgetting: Coordinator(() => removed = true));
        vm.RequestForgetCommand.Execute(null);

        vm.CancelForgetCommand.Execute(null);

        Assert.False(vm.IsForgetPending);
        Assert.False(removed);
        Assert.Equal(string.Empty, vm.ForgetStatusText);
    }

    [Fact]
    public void Confirming_removes_the_stored_data_and_says_so()
    {
        var removed = false;
        var vm = new CaptureRig().CreateViewModel(forgetting: Coordinator(() => removed = true));
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.True(removed);
        Assert.False(vm.IsForgetPending);
        Assert.Equal(GameDataViewModel.ForgetCompleteText, vm.ForgetStatusText);
    }

    [Fact]
    public void A_failed_removal_is_reported_and_nothing_is_claimed_forgotten()
    {
        var vm = new CaptureRig().CreateViewModel(forgetting: Coordinator(() => false));
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.Equal(GameDataViewModel.ForgetFailedText, vm.ForgetStatusText);
    }

    [Fact]
    public void Features_that_could_not_be_forgotten_are_named()
    {
        var vm = new CaptureRig().CreateViewModel(forgetting: Coordinator(() => true, new ThrowingForgettable("Reputation")));
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.Contains("Reputation", vm.ForgetStatusText);
        Assert.StartsWith(GameDataViewModel.ForgetCompleteText, vm.ForgetStatusText);
    }

    private sealed class ThrowingForgettable(string name) : IForgettable
    {
        public string Name { get; } = name;

        public void Quiesce()
        {
        }

        public void Resume()
        {
        }

        public void Forget() => throw new InvalidOperationException();
    }
}
