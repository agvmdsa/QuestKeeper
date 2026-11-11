using AqwCompanion.App.Feed;
using AqwCompanion.App.Infrastructure.AutoStart;
using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AqwCompanion.Tests.Settings;

public class SettingsWindowTests
{
    private static (SettingsWindow Window, SettingsViewModel Vm, FakeAppSettingsStore Store, bool[] Removed) Open(IAutoStart? autoStart = null)
    {
        var removed = new bool[1];
        var store = new FakeAppSettingsStore();
        var vm = new SettingsViewModel(store, new ForgetCoordinator(() => removed[0] = true, () => []), autoStart);
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
        Assert.Equal("Questkeeper " + vm.Version, window.FindControl<TextBlock>("SettingsVersion")!.Text);
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
    public void The_feed_section_is_hidden_when_the_app_has_no_feed()
    {
        var (window, _, _, _) = Open();

        Assert.False(window.FindControl<Border>("SettingsFeedSection")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_feed_section_shows_the_switch_the_status_the_secret_file_and_the_new_secret_button()
    {
        var path = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"), "feed.token");
        try
        {
            var store = new FakeAppSettingsStore();
            var secret = new FeedSecretStore(path);
            var vm = new SettingsViewModel(store, new ForgetCoordinator(() => true, () => []), null, secret);
            vm.SetFeedStatus("Listening on 127.0.0.1:47821");
            var window = new SettingsWindow { DataContext = vm };
            window.Show();

            Assert.True(window.FindControl<Border>("SettingsFeedSection")!.IsEffectivelyVisible);
            Assert.Equal("Listening on 127.0.0.1:47821", window.FindControl<TextBlock>("SettingsFeedStatus")!.Text);
            Assert.Contains(path, window.FindControl<SelectableTextBlock>("SettingsFeedPath")!.Text);

            window.FindControl<ToggleSwitch>("SettingsFeedSwitch")!.IsChecked = true;
            Assert.True(store.Current.LocalFeed);

            var button = window.FindControl<Button>("SettingsFeedNewSecret")!;
            Assert.True(button.IsEffectivelyEnabled);
            var old = secret.GetOrCreate();
            button.Command!.Execute(null);
            Assert.NotEqual(old, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static SettingsWindow OpenWithPhone(bool debug, out SettingsViewModel vm, out FakeAppSettingsStore store)
    {
        store = new FakeAppSettingsStore(new AppSettings(DebugMode: debug));
        var registry = new PhoneRegistry(new MemoryPhoneFileStore(), () => DateTimeOffset.UtcNow);
        vm = new SettingsViewModel(store, new ForgetCoordinator(() => true, () => []), null, null, registry, _ => Task.CompletedTask);
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void The_phone_section_is_hidden_when_the_app_has_no_phone_access()
    {
        var (window, _, _, _) = Open();

        Assert.False(window.FindControl<Border>("SettingsPhoneSection")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_phone_section_shows_the_switch_and_the_status_and_hides_the_debug_link_unless_debug_mode_is_on()
    {
        var window = OpenWithPhone(debug: false, out var vm, out var store);
        vm.SetPhoneStatus("Listening on 127.0.0.1:47822");

        Assert.True(window.FindControl<Border>("SettingsPhoneSection")!.IsEffectivelyVisible);
        Assert.Equal("Listening on 127.0.0.1:47822", window.FindControl<TextBlock>("SettingsPhoneStatus")!.Text);
        Assert.False(window.FindControl<Button>("SettingsPhoneCopyLink")!.IsEffectivelyVisible);

        window.FindControl<ToggleSwitch>("SettingsPhoneSwitch")!.IsChecked = true;
        Assert.True(store.Current.PhoneAccess);

        vm.DebugMode = true;
        Assert.True(window.FindControl<Button>("SettingsPhoneCopyLink")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_pair_button_is_enabled_only_when_a_pairing_link_can_be_made()
    {
        var window = OpenWithPhone(debug: false, out var vm, out _);
        var button = window.FindControl<Button>("SettingsPhonePair")!;
        Assert.Equal("Pair a phone...", button.Content);
        Assert.False(button.IsEffectivelyEnabled);

        vm.SetPhoneTailscale("Ready.", true);
        Assert.True(button.IsEffectivelyEnabled);
        var asked = 0;
        vm.PairRequested += (_, _) => asked++;
        button.Command!.Execute(null);
        Assert.Equal(1, asked);

        vm.SetPhoneTailscale("Tailscale is not installed.", false);
        Assert.False(button.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void The_phone_section_shows_the_tailscale_sentence_only_when_there_is_one()
    {
        var window = OpenWithPhone(debug: false, out var vm, out _);
        var text = window.FindControl<TextBlock>("SettingsPhoneTailscale")!;
        Assert.False(text.IsEffectivelyVisible);

        vm.SetPhoneTailscale("Tailscale is not installed.", false);

        Assert.True(text.IsEffectivelyVisible);
        Assert.Equal("Tailscale is not installed.", text.Text);
        Assert.False(vm.CanPair);
    }

    [AvaloniaFact]
    public void The_start_with_windows_section_is_hidden_where_it_is_not_supported()
    {
        var (window, _, _, _) = Open();

        Assert.False(window.FindControl<Border>("SettingsAutoStartSection")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_start_with_windows_switch_sets_the_task_and_shows_a_failure()
    {
        var auto = new FakeAutoStart();
        var (window, vm, _, _) = Open(auto);
        vm.Refresh();
        var section = window.FindControl<Border>("SettingsAutoStartSection")!;
        var toggle = window.FindControl<ToggleSwitch>("SettingsAutoStartSwitch")!;
        Assert.True(section.IsEffectivelyVisible);
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;
        Assert.True(auto.Enabled);
        Assert.False(window.FindControl<TextBlock>("SettingsAutoStartError")!.IsEffectivelyVisible);

        auto.Fails = true;
        toggle.IsChecked = false;

        Assert.True(auto.Enabled);
        var error = window.FindControl<TextBlock>("SettingsAutoStartError")!;
        Assert.True(error.IsEffectivelyVisible);
        Assert.Equal(SettingsViewModel.AutoStartFailedText, error.Text);
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

    [AvaloniaFact]
    public void The_skua_block_shows_the_texts_the_status_and_two_named_buttons()
    {
        var (window, vm, _, _) = Open();
        vm.SetSkuaStatus("Skua is connecting...");

        Assert.Equal(SettingsViewModel.SkuaStepsText, window.FindControl<TextBlock>("SettingsSkuaSteps")!.Text);
        Assert.Equal("Skua is connecting...", window.FindControl<TextBlock>("SettingsSkuaStatus")!.Text);
        foreach (var name in new[] { "SettingsSkuaOpenFolder", "SettingsSkuaOpenRelease" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.True(button.IsEffectivelyEnabled);
            Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(button)));
        }
    }
}
