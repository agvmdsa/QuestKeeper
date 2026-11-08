using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AqwCompanion.Tests.Settings;

public class SettingsWindowTests
{
    private static (SettingsWindow Window, SettingsViewModel Vm, FakeAppSettingsStore Store, bool[] Removed) Open()
    {
        var removed = new bool[1];
        var store = new FakeAppSettingsStore();
        var vm = new SettingsViewModel(store, new ForgetCoordinator(() => removed[0] = true, () => []));
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        return (window, vm, store, removed);
    }

    [AvaloniaFact]
    public void The_dialog_shows_the_privacy_text_the_credits_and_the_version()
    {
        var (window, vm, _, _) = Open();

        Assert.Equal(SettingsViewModel.PrivacyText, window.FindControl<TextBlock>("SettingsPrivacyText")!.Text);
        Assert.Equal(SettingsViewModel.CreditsText, window.FindControl<TextBlock>("SettingsCreditsText")!.Text);
        Assert.Equal("AQW Companion " + vm.Version, window.FindControl<TextBlock>("SettingsVersion")!.Text);
    }

    [AvaloniaFact]
    public void The_debug_switch_follows_and_changes_the_setting()
    {
        var (window, vm, store, _) = Open();
        var toggle = window.FindControl<ToggleSwitch>("SettingsDebugSwitch")!;
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;

        Assert.True(vm.DebugMode);
        Assert.True(store.Current.DebugMode);
    }

    [AvaloniaFact]
    public void Forget_my_data_asks_first_then_removes_and_reports()
    {
        var (window, vm, _, removed) = Open();
        var ask = window.FindControl<StackPanel>("SettingsForgetAsk")!;
        var confirm = window.FindControl<StackPanel>("SettingsForgetConfirm")!;
        Assert.True(ask.IsVisible);
        Assert.False(confirm.IsVisible);

        vm.RequestForgetCommand.Execute(null);

        Assert.False(ask.IsVisible);
        Assert.True(confirm.IsVisible);
        Assert.False(removed[0]);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.True(removed[0]);
        Assert.True(ask.IsVisible);
        Assert.Equal(SettingsViewModel.ForgetCompleteText, window.FindControl<TextBlock>("SettingsForgetStatus")!.Text);
    }

    [AvaloniaFact]
    public void Close_asks_the_window_to_close()
    {
        var (window, vm, _, _) = Open();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        window.FindControl<Button>("SettingsCloseButton")!.Command!.Execute(null);

        Assert.True(closed);
    }
}
