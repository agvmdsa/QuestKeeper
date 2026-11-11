using AqwCompanion.App;
using AqwCompanion.App.Feed;
using AqwCompanion.App.Infrastructure.AutoStart;
using AqwCompanion.App.Settings;
using AqwCompanion.App.Skua;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Settings;

public class SettingsViewModelTests
{
    private static ForgetCoordinator Coordinator(Func<bool> removeStoredData, params IForgettable[] participants) =>
        new(removeStoredData, () => participants);

    private static SettingsViewModel Build(
        ForgetCoordinator? forgetting = null, FakeAppSettingsStore? store = null, IAutoStart? autoStart = null, FeedSecretStore? feedSecret = null) =>
        new(store ?? new FakeAppSettingsStore(), forgetting ?? Coordinator(() => true), autoStart, feedSecret);

    [Fact]
    public void Debug_mode_starts_from_the_saved_setting()
    {
        Assert.True(Build(store: new FakeAppSettingsStore(new AppSettings(DebugMode: true))).DebugMode);
        Assert.False(Build().DebugMode);
    }

    [Fact]
    public void Turning_debug_mode_on_keeps_the_saved_phone_access_setting()
    {
        var store = new FakeAppSettingsStore(new AppSettings(PhoneAccess: true));

        Build(store: store).DebugMode = true;

        Assert.True(store.Current.DebugMode);
        Assert.True(store.Current.PhoneAccess);
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
        Assert.Contains("Phone access", SettingsViewModel.PrivacyText);
        Assert.Contains("Tailscale", SettingsViewModel.PrivacyText);
        Assert.DoesNotContain("Nothing leaves this computer", SettingsViewModel.PrivacyText);
        Assert.Contains("Artix Entertainment", SettingsViewModel.CreditsText);
        Assert.Contains("CC BY-SA", SettingsViewModel.CreditsText);
        Assert.Equal(SettingsViewModel.DisplayVersion(AppInfo.Version), Build().Version);
    }

    [Theory]
    [InlineData("0.1.1-dev.92+b84475d569754d8820c0adc8f9250aaddd5ba596", "0.1.1-dev.92")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("", "0.0.0")]
    public void The_version_is_shown_without_the_build_hash(string full, string expected)
    {
        Assert.Equal(expected, SettingsViewModel.DisplayVersion(full));
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
    public void The_local_feed_starts_from_the_saved_setting_and_is_off_by_default()
    {
        Assert.False(Build().LocalFeedEnabled);
        Assert.True(Build(store: new FakeAppSettingsStore(new AppSettings(LocalFeed: true))).LocalFeedEnabled);
    }

    [Fact]
    public void Turning_the_local_feed_on_saves_it_and_tells_the_app_once()
    {
        var store = new FakeAppSettingsStore();
        var vm = Build(store: store);
        var changes = 0;
        vm.LocalFeedChanged += (_, _) => changes++;

        vm.LocalFeedEnabled = true;
        vm.LocalFeedEnabled = true;

        Assert.True(store.Current.LocalFeed);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Changing_one_setting_keeps_the_other()
    {
        var store = new FakeAppSettingsStore(new AppSettings(DebugMode: false, LocalFeed: true));
        var vm = Build(store: store);

        vm.DebugMode = true;
        Assert.True(store.Current.LocalFeed);
        Assert.True(store.Current.DebugMode);

        vm.LocalFeedEnabled = false;
        Assert.True(store.Current.DebugMode);
        Assert.False(store.Current.LocalFeed);
    }

    [Fact]
    public void Without_a_feed_the_new_secret_command_is_not_available()
    {
        var vm = Build();

        Assert.False(vm.HasFeed);
        Assert.False(vm.RegenerateFeedSecretCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.FeedSecretPath);
    }

    [Fact]
    public void A_new_secret_replaces_the_file_and_tells_the_app()
    {
        var path = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"), "feed.token");
        try
        {
            var secret = new FeedSecretStore(path);
            var old = secret.GetOrCreate();
            var vm = Build(feedSecret: secret);
            var raised = 0;
            vm.FeedSecretRegenerated += (_, _) => raised++;

            vm.RegenerateFeedSecretCommand.Execute(null);

            Assert.Equal(path, vm.FeedSecretPath);
            Assert.NotEqual(old, File.ReadAllText(path));
            Assert.Equal(1, raised);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
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

    private static (SettingsViewModel Vm, PhoneRegistry Registry, List<string> Copied) BuildWithPhone(bool debug, Func<string, Task>? copy = null, Func<bool>? available = null)
    {
        var registry = new PhoneRegistry(new MemoryPhoneFileStore(), () => DateTimeOffset.UtcNow);
        var copied = new List<string>();
        var vm = new SettingsViewModel(
            new FakeAppSettingsStore(new AppSettings(DebugMode: debug)), Coordinator(() => true), null, null,
            registry, copy ?? (text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            }), available);
        return (vm, registry, copied);
    }

    [Fact]
    public void The_phone_switch_is_saved_and_raises_the_event()
    {
        var store = new FakeAppSettingsStore();
        var vm = Build(store: store);
        var raised = 0;
        vm.PhoneAccessChanged += (_, _) => raised++;
        Assert.False(vm.PhoneAccessEnabled);

        vm.PhoneAccessEnabled = true;
        vm.PhoneAccessEnabled = true;

        Assert.True(store.Current.PhoneAccess);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void The_phone_status_defaults_to_off_and_can_be_set()
    {
        var vm = Build();
        Assert.Equal("Off", vm.PhoneStatusText);

        vm.SetPhoneStatus("Listening on 127.0.0.1:47822");

        Assert.Equal("Listening on 127.0.0.1:47822", vm.PhoneStatusText);
    }

    [Fact]
    public void Without_a_registry_there_is_no_phone_section_and_no_local_link()
    {
        var vm = Build();

        Assert.False(vm.HasPhone);
        Assert.False(vm.CopyLocalPairingLinkCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_debug_command_puts_a_local_pairing_link_on_the_clipboard_only_in_debug_mode()
    {
        var (off, _, copiedOff) = BuildWithPhone(debug: false);
        Assert.True(off.HasPhone);
        Assert.False(off.IsLocalPairingLinkVisible);
        Assert.False(off.CopyLocalPairingLinkCommand.CanExecute(null));
        await off.CopyLocalPairingLinkCommand.ExecuteAsync(null);
        Assert.Empty(copiedOff);

        var (on, registry, copied) = BuildWithPhone(debug: true);
        Assert.True(on.IsLocalPairingLinkVisible);
        await on.CopyLocalPairingLinkCommand.ExecuteAsync(null);

        var link = Assert.Single(copied);
        Assert.Matches(@"^http://127\.0\.0\.1:47822/#t=[A-Za-z0-9_-]{43}$", link);
        Assert.Equal(PairStatus.Paired, registry.TryPair(link[(link.IndexOf("#t=", StringComparison.Ordinal) + 3)..], "Test").Status);
        Assert.DoesNotContain("#t=", on.PhoneStatusText);
    }

    [Fact]
    public async Task No_pairing_token_is_made_when_there_is_no_clipboard()
    {
        var (vm, _, copied) = BuildWithPhone(debug: true, available: () => false);

        await vm.CopyLocalPairingLinkCommand.ExecuteAsync(null);

        Assert.Empty(copied);
    }

    [Fact]
    public async Task A_throwing_clipboard_does_not_escape_the_command()
    {
        var (vm, _, _) = BuildWithPhone(debug: true, copy: _ => throw new InvalidOperationException("boom"));

        var ex = await Record.ExceptionAsync(() => vm.CopyLocalPairingLinkCommand.ExecuteAsync(null));

        Assert.Null(ex);
    }

    private sealed class FakeFolders(bool result = true) : IFolderLauncher
    {
        public List<string> Opened { get; } = [];

        public bool Open(string path)
        {
            Opened.Add(path);
            return result;
        }
    }

    private sealed class FakeRelease(bool result = true) : IReleasePageLauncher
    {
        public List<string> Opened { get; } = [];

        public bool Open(string url)
        {
            Opened.Add(url);
            return result;
        }
    }

    private static SettingsViewModel BuildSkua(IReleasePageLauncher release, IFolderLauncher folders, bool exists) =>
        new(new FakeAppSettingsStore(), Coordinator(() => true), null, null, null, null, null, release, folders, "/skua/plugins", _ => exists);

    [Fact]
    public void The_skua_status_can_be_set_and_raises_a_change()
    {
        var vm = Build();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SetSkuaStatus("Skua is connecting...");

        Assert.Equal("Skua is connecting...", vm.SkuaStatusText);
        Assert.Contains(nameof(SettingsViewModel.SkuaStatusText), changed);
    }

    [Fact]
    public void The_plugins_folder_is_opened_when_it_exists()
    {
        var folders = new FakeFolders();
        var vm = BuildSkua(new FakeRelease(), folders, exists: true);
        var before = vm.SkuaStatusText;

        vm.OpenSkuaPluginsFolderCommand.Execute(null);

        Assert.Equal(["/skua/plugins"], folders.Opened);
        Assert.Equal(before, vm.SkuaStatusText);
    }

    [Fact]
    public void A_missing_plugins_folder_is_not_opened_or_created()
    {
        var folders = new FakeFolders();
        var vm = BuildSkua(new FakeRelease(), folders, exists: false);

        vm.OpenSkuaPluginsFolderCommand.Execute(null);

        Assert.Empty(folders.Opened);
        Assert.Equal("Skua's plugins folder was not found. Open Skua once, then try again.", vm.SkuaStatusText);
    }

    [Fact]
    public void A_failed_folder_launch_is_reported()
    {
        var vm = BuildSkua(new FakeRelease(), new FakeFolders(false), exists: true);

        vm.OpenSkuaPluginsFolderCommand.Execute(null);

        Assert.Equal(SettingsViewModel.SkuaFolderFailedText, vm.SkuaStatusText);
    }

    [Fact]
    public void The_release_page_is_opened_with_the_fixed_url()
    {
        var release = new FakeRelease();
        var vm = BuildSkua(release, new FakeFolders(), exists: true);

        vm.OpenReleasePageCommand.Execute(null);

        Assert.Equal(["https://github.com/agvmdsa/QuestKeeper/releases/latest"], release.Opened);
    }

    [Fact]
    public void The_skua_texts_are_set_and_make_no_gold_or_inventory_claim()
    {
        string[] texts =
        [
            SettingsViewModel.SkuaTitleText, SettingsViewModel.SkuaDescriptionText, SettingsViewModel.SkuaStepsText,
            SettingsViewModel.OpenSkuaPluginsFolderText, SettingsViewModel.OpenReleasePageText,
        ];

        Assert.All(texts, t =>
        {
            Assert.NotEmpty(t);
            Assert.DoesNotContain("gold", t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("inventory", t, StringComparison.OrdinalIgnoreCase);
        });
    }
}
