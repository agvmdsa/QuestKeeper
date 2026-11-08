using AqwCompanion.App;
using AqwCompanion.App.Infrastructure.AutoStart;
using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Settings;

public class SettingsViewModelTests
{
    private static ForgetCoordinator Coordinator(Func<bool> removeStoredData, params IForgettable[] participants) =>
        new(removeStoredData, () => participants);

    private static SettingsViewModel Build(
        ForgetCoordinator? forgetting = null, FakeAppSettingsStore? store = null, IAutoStart? autoStart = null) =>
        new(store ?? new FakeAppSettingsStore(), forgetting ?? Coordinator(() => true), autoStart);

    [Fact]
    public void Debug_mode_starts_from_the_saved_setting()
    {
        Assert.True(Build(store: new FakeAppSettingsStore(new AppSettings(DebugMode: true))).DebugMode);
        Assert.False(Build().DebugMode);
    }

    [Fact]
    public void Turning_debug_mode_on_saves_it_and_tells_the_app()
    {
        var store = new FakeAppSettingsStore();
        var vm = Build(store: store);
        var changes = 0;
        vm.DebugModeChanged += (_, _) => changes++;

        vm.DebugMode = true;

        Assert.True(store.Current.DebugMode);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Setting_the_same_value_again_saves_nothing()
    {
        var store = new FakeAppSettingsStore();
        var vm = Build(store: store);

        vm.DebugMode = false;

        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void The_texts_state_what_the_app_reads_keeps_and_never_keeps()
    {
        Assert.Contains("never", SettingsViewModel.PrivacyText);
        Assert.Contains("e-mail", SettingsViewModel.PrivacyText);
        Assert.Contains("other players", SettingsViewModel.PrivacyText);
        Assert.Contains("Artix Entertainment", SettingsViewModel.CreditsText);
        Assert.Contains("CC BY-SA", SettingsViewModel.CreditsText);
        Assert.Equal(AppInfo.Version, Build().Version);
    }

    [Fact]
    public void Requesting_asks_for_confirmation_and_removes_nothing()
    {
        var removed = false;
        var vm = Build(Coordinator(() => removed = true));

        vm.RequestForgetCommand.Execute(null);

        Assert.True(vm.IsForgetPending);
        Assert.False(removed);
    }

    [Fact]
    public void Cancelling_keeps_the_data()
    {
        var removed = false;
        var vm = Build(Coordinator(() => removed = true));
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
        var vm = Build(Coordinator(() => removed = true));
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.True(removed);
        Assert.False(vm.IsForgetPending);
        Assert.Equal(SettingsViewModel.ForgetCompleteText, vm.ForgetStatusText);
    }

    [Fact]
    public void Forgetting_keeps_the_settings()
    {
        var store = new FakeAppSettingsStore(new AppSettings(DebugMode: true));
        var vm = Build(store: store);

        vm.RequestForgetCommand.Execute(null);
        vm.ConfirmForgetCommand.Execute(null);

        Assert.True(vm.DebugMode);
        Assert.True(store.Current.DebugMode);
    }

    [Fact]
    public void A_failed_removal_is_reported_and_nothing_is_claimed_forgotten()
    {
        var vm = Build(Coordinator(() => false));
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.Equal(SettingsViewModel.ForgetFailedText, vm.ForgetStatusText);
    }

    [Fact]
    public void Features_that_could_not_be_forgotten_are_named()
    {
        var vm = Build(Coordinator(() => true, new ThrowingForgettable("Reputation file")));
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.Contains("Reputation file", vm.ForgetStatusText);
        Assert.StartsWith(SettingsViewModel.ForgetCompleteText, vm.ForgetStatusText);
    }

    [Fact]
    public void Without_a_supported_auto_start_the_switch_is_not_offered()
    {
        var vm = Build();

        Assert.False(vm.CanAutoStart);
        Assert.False(vm.StartWithWindows);
    }

    [Fact]
    public void Refreshing_reads_whether_the_task_exists()
    {
        var auto = new FakeAutoStart { Enabled = true };
        var vm = Build(autoStart: auto);
        Assert.False(vm.StartWithWindows);

        vm.Refresh();

        Assert.True(vm.CanAutoStart);
        Assert.True(vm.StartWithWindows);
    }

    [Fact]
    public void Turning_start_with_windows_on_and_off_sets_the_task()
    {
        var auto = new FakeAutoStart();
        var vm = Build(autoStart: auto);

        vm.StartWithWindows = true;
        Assert.True(auto.Enabled);
        Assert.Equal(string.Empty, vm.AutoStartError);

        vm.StartWithWindows = false;
        Assert.False(auto.Enabled);
    }

    [Fact]
    public void A_failed_change_goes_back_and_says_so()
    {
        var auto = new FakeAutoStart { Fails = true };
        var vm = Build(autoStart: auto);

        vm.StartWithWindows = true;

        Assert.False(vm.StartWithWindows);
        Assert.Equal(SettingsViewModel.AutoStartFailedText, vm.AutoStartError);
        Assert.True(vm.HasAutoStartError);
    }

    [Fact]
    public void Closing_raises_the_close_request()
    {
        var vm = Build();
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.CloseCommand.Execute(null);

        Assert.Equal(1, closed);
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
