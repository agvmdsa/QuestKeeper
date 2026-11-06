using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Account;

public class AccountViewModelTests
{
    private readonly FakeSignInDialog _dialog = new();

    private AccountViewModel Build() => new(_dialog);

    private static Func<SignInMode, Task<SignInResult>> Reports(SignInResult result) =>
        _ => Task.FromResult(result);

    [Fact]
    public void Starts_not_connected_and_does_not_open_the_dialog()
    {
        var vm = Build();

        Assert.Equal("Not connected", vm.Headline);
        Assert.False(vm.IsConnected);
        Assert.True(vm.NeedsSignIn);
        Assert.Null(vm.CharacterName);
        Assert.Equal(AccountViewModel.SignedOutMessage, vm.Status);
        Assert.Empty(_dialog.Shown);
    }

    [Fact]
    public async Task Sign_in_opens_the_full_dialog_and_shows_the_connected_name()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal([SignInMode.Full], _dialog.Shown);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal("Connected as Test Hero", vm.Headline);
        Assert.True(vm.IsConnected);
        Assert.False(vm.NeedsSignIn);
        Assert.Equal(AccountViewModel.ReadMessage, vm.Status);
    }

    [Fact]
    public async Task Refresh_opens_the_compact_dialog_and_updates_the_name()
    {
        var vm = Build();
        _dialog.OnShow = Reports(SignInResult.Connected("Old Hero"));
        await vm.SignInCommand.ExecuteAsync(null);
        _dialog.OnShow = Reports(SignInResult.Connected("New Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal([SignInMode.Full, SignInMode.Compact], _dialog.Shown);
        Assert.Equal("New Hero", vm.CharacterName);
        Assert.Equal("Connected as New Hero", vm.Headline);
    }

    [Fact]
    public async Task A_cancelled_sign_in_changes_no_name()
    {
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Null(vm.CharacterName);
        Assert.Equal("Not connected", vm.Headline);
        Assert.Equal(AccountViewModel.CancelledMessage, vm.Status);
    }

    [Fact]
    public async Task A_cancelled_refresh_keeps_the_known_name()
    {
        var vm = Build();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        await vm.SignInCommand.ExecuteAsync(null);
        _dialog.OnShow = Reports(SignInResult.Cancelled());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal("Connected as Test Hero", vm.Headline);
        Assert.Equal(AccountViewModel.CancelledMessage, vm.Status);
    }

    [Fact]
    public async Task A_failed_refresh_shows_the_message_and_keeps_the_last_name()
    {
        var vm = Build();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        await vm.SignInCommand.ExecuteAsync(null);
        _dialog.OnShow = Reports(SignInResult.Failed("The page took too long to read. Try again."));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal("The page took too long to read. Try again.", vm.Status);
    }

    [Fact]
    public async Task A_failed_sign_in_without_a_message_uses_the_fallback()
    {
        _dialog.OnShow = Reports(new SignInResult(SignInOutcome.Failed));
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Null(vm.CharacterName);
        Assert.Equal(AccountViewModel.FailedFallbackMessage, vm.Status);
    }

    [Fact]
    public async Task A_dialog_that_throws_is_reported_in_the_status()
    {
        _dialog.OnShow = _ => throw new InvalidOperationException("no window");
        var vm = Build();

        var error = await Record.ExceptionAsync(() => vm.SignInCommand.ExecuteAsync(null));

        Assert.Null(error);
        Assert.Contains("no window", vm.Status, StringComparison.Ordinal);
        Assert.Null(vm.CharacterName);
    }

    [Fact]
    public async Task A_second_request_while_the_dialog_is_open_is_ignored()
    {
        var gate = new TaskCompletionSource<SignInResult>();
        _dialog.OnShow = _ => gate.Task;
        var vm = Build();

        var first = vm.SignInCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);
        gate.SetResult(SignInResult.Connected("Test Hero"));
        await first;

        Assert.Equal([SignInMode.Full], _dialog.Shown);
        Assert.Equal("Test Hero", vm.CharacterName);
    }

    [Fact]
    public async Task Connecting_raises_change_notifications_for_the_card()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Contains(nameof(AccountViewModel.Headline), changed);
        Assert.Contains(nameof(AccountViewModel.IsConnected), changed);
        Assert.Contains(nameof(AccountViewModel.NeedsSignIn), changed);
        Assert.Contains(nameof(AccountViewModel.Status), changed);
    }

    private static CharacterEquipment Equipment(string name, int? characterId) =>
        new(name, 10, null, null, null, null, null, [new EquipmentSlot("Armor", "Test Armor", null, false)], [], characterId);

    private static CharacterBadges Badges(int count) =>
        new(Enumerable.Range(1, count)
            .Select(i => new Badge($"Test Badge {i}", $"Description {i}", i <= 2 ? "Quests" : "Events"))
            .ToList());

    private static Func<string, Task<EquipmentLoadResult>> Equips(CharacterEquipment equipment) =>
        _ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Loaded, equipment));

    private static Func<int, Task<BadgesLoadResult>> HasBadges(CharacterBadges badges) =>
        _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Loaded, badges));

    private async Task<AccountViewModel> ConnectedAsync(
        Func<string, Task<EquipmentLoadResult>> equipment, Func<int, Task<BadgesLoadResult>> badges)
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = new AccountViewModel(_dialog, new FakeEquipmentLoader(equipment), new FakeBadgesLoader(badges));
        await vm.SignInCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Loads_the_badges_for_the_character_id_after_the_equipment()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(7)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = new AccountViewModel(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))), badgesLoader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal([3], badgesLoader.Requested);
        Assert.True(vm.HasBadges);
        Assert.Equal("7 badges", vm.BadgeCountText);
        Assert.Equal(5, vm.BadgesPreview.Count());
        Assert.Equal("Test Badge 1", vm.BadgesPreview.First().Title);
        Assert.Equal(["Quests", "Events"], vm.BadgeGroups.Select(g => g.Category));
        Assert.Equal(string.Empty, vm.BadgesStatus);
        Assert.False(vm.HasBadgesStatus);
        Assert.True(vm.OpenBadgesDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_single_badge_uses_the_singular_count()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(1)));

        Assert.Equal("1 badge", vm.BadgeCountText);
    }

    [Fact]
    public async Task Shows_the_loading_text_while_the_badges_are_read()
    {
        var gate = new TaskCompletionSource<BadgesLoadResult>();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = new AccountViewModel(
            _dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))), new FakeBadgesLoader(_ => gate.Task));

        var signIn = vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal(AccountViewModel.BadgesLoadingText, vm.BadgesStatus);
        Assert.True(vm.HasBadgesStatus);
        Assert.False(vm.HasBadges);
        gate.SetResult(new BadgesLoadResult(BadgesLoadStatus.Loaded, Badges(2)));
        await signIn;
        Assert.Equal(string.Empty, vm.BadgesStatus);
        Assert.True(vm.HasBadges);
    }

    [Fact]
    public async Task An_empty_badge_list_shows_the_none_found_text_and_cannot_open_the_sidebar()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(0)));

        Assert.False(vm.HasBadges);
        Assert.Equal(AccountViewModel.BadgesNotFoundText, vm.BadgesStatus);
        Assert.False(vm.OpenBadgesDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Badges_that_cannot_be_read_show_the_none_found_text()
    {
        var vm = await ConnectedAsync(
            Equips(Equipment("Test Hero", 3)), _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.NotFound)));

        Assert.False(vm.HasBadges);
        Assert.Equal(AccountViewModel.BadgesNotFoundText, vm.BadgesStatus);
    }

    [Fact]
    public async Task Badges_that_cannot_be_fetched_show_the_unavailable_text()
    {
        var vm = await ConnectedAsync(
            Equips(Equipment("Test Hero", 3)), _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Unavailable)));

        Assert.False(vm.HasBadges);
        Assert.Equal(AccountViewModel.BadgesUnavailableText, vm.BadgesStatus);
    }

    [Fact]
    public async Task Without_a_character_id_the_badges_are_not_requested()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = new AccountViewModel(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", null))), badgesLoader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Empty(badgesLoader.Requested);
        Assert.True(vm.HasEquipment);
        Assert.Equal(AccountViewModel.BadgesUnavailableText, vm.BadgesStatus);
    }

    [Fact]
    public async Task When_the_equipment_is_not_loaded_the_badges_are_not_requested()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = new AccountViewModel(
            _dialog,
            new FakeEquipmentLoader(_ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Unavailable))),
            badgesLoader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Empty(badgesLoader.Requested);
        Assert.Equal(AccountViewModel.BadgesUnavailableText, vm.BadgesStatus);
    }

    [Fact]
    public async Task Badges_are_requested_only_by_sign_in_and_refresh()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = new AccountViewModel(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))), badgesLoader);
        await vm.SignInCommand.ExecuteAsync(null);

        vm.OpenBadgesDetailsCommand.Execute(null);
        vm.CloseSidebarCommand.Execute(null);
        Assert.Single(badgesLoader.Requested);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal([3, 3], badgesLoader.Requested);
    }

    [Fact]
    public async Task Each_card_opens_the_sidebar_with_its_own_title()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(3)));
        Assert.False(vm.IsSidebarOpen);

        vm.OpenBadgesDetailsCommand.Execute(null);

        Assert.True(vm.IsSidebarOpen);
        Assert.Equal("Badges", vm.SidebarTitle);
        Assert.Equal("Badges details", vm.SidebarAutomationName);
        Assert.True(vm.IsBadgesSidebar);
        Assert.False(vm.IsEquippedSidebar);

        vm.CloseSidebarCommand.Execute(null);
        vm.OpenEquippedDetailsCommand.Execute(null);

        Assert.True(vm.IsSidebarOpen);
        Assert.Equal("Equipped", vm.SidebarTitle);
        Assert.Equal("Equipped details", vm.SidebarAutomationName);
        Assert.True(vm.IsEquippedSidebar);
        Assert.False(vm.IsBadgesSidebar);
    }

    [Fact]
    public async Task Clearing_the_badges_closes_the_badges_sidebar()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(3)));
        vm.OpenBadgesDetailsCommand.Execute(null);

        vm.Badges = null;

        Assert.False(vm.IsSidebarOpen);
        Assert.False(vm.OpenBadgesDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Clearing_the_badges_leaves_the_equipped_sidebar_open()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(3)));
        vm.OpenEquippedDetailsCommand.Execute(null);

        vm.Badges = null;

        Assert.True(vm.IsSidebarOpen);
    }

    [Fact]
    public async Task Clearing_the_equipment_leaves_the_badges_sidebar_open()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(3)));
        vm.OpenBadgesDetailsCommand.Execute(null);

        vm.Equipment = null;

        Assert.True(vm.IsSidebarOpen);
        Assert.True(vm.IsBadgesSidebar);
    }

    [Fact]
    public async Task A_different_character_clears_the_previous_badges()
    {
        var vm = await ConnectedAsync(
            name => Task.FromResult(name == "Test Hero"
                ? new EquipmentLoadResult(EquipmentLoadStatus.Loaded, Equipment(name, 3))
                : new EquipmentLoadResult(EquipmentLoadStatus.Unavailable)),
            HasBadges(Badges(3)));
        vm.OpenBadgesDetailsCommand.Execute(null);
        _dialog.OnShow = Reports(SignInResult.Connected("Other Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.IsSidebarOpen);
        Assert.False(vm.HasBadges);
    }

    [Fact]
    public async Task Without_a_badges_loader_the_badges_card_stays_empty()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = new AccountViewModel(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.HasEquipment);
        Assert.False(vm.HasBadges);
        Assert.Equal(string.Empty, vm.BadgesStatus);
    }

    [Fact]
    public void Without_a_summary_the_card_texts_are_dashes()
    {
        var vm = Build();

        Assert.False(vm.HasSummary);
        Assert.Equal(AccountViewModel.MissingValueText, vm.AdventureCoinsText);
        Assert.Equal(AccountViewModel.MissingValueText, vm.AccountTypeText);
        Assert.Equal(AccountViewModel.MissingValueText, vm.MembershipText);
        Assert.Equal(AccountViewModel.MissingValueText, vm.GearsOfDoomText);
    }

    [Fact]
    public async Task Sign_in_shows_the_summary_with_thousands_separators()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.HasSummary);
        Assert.Equal("1,234,567", vm.AdventureCoinsText);
        Assert.Equal("LEGEND", vm.AccountTypeText);
        Assert.Equal("Membership Expired: Tuesday, January 2, 2030", vm.MembershipText);
        Assert.Equal("7", vm.GearsOfDoomText);
    }

    [Fact]
    public async Task Missing_summary_fields_show_a_dash_while_the_others_show()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", new AccountSummary(5, null, null, null)));
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.HasSummary);
        Assert.Equal("5", vm.AdventureCoinsText);
        Assert.Equal(AccountViewModel.MissingValueText, vm.AccountTypeText);
        Assert.Equal(AccountViewModel.MissingValueText, vm.MembershipText);
        Assert.Equal(AccountViewModel.MissingValueText, vm.GearsOfDoomText);
    }

    [Fact]
    public async Task A_connected_result_without_a_summary_leaves_the_card_unavailable()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.False(vm.HasSummary);
    }

    [Fact]
    public async Task A_failed_or_cancelled_refresh_keeps_the_last_summary()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        var vm = Build();
        await vm.SignInCommand.ExecuteAsync(null);

        _dialog.OnShow = Reports(SignInResult.Failed("The page took too long to read. Try again."));
        await vm.RefreshCommand.ExecuteAsync(null);
        _dialog.OnShow = Reports(SignInResult.Cancelled());
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(SampleAccount.Summary, vm.Summary);
    }

    [Fact]
    public async Task A_refresh_replaces_the_summary()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        var vm = Build();
        await vm.SignInCommand.ExecuteAsync(null);
        var newer = new AccountSummary(10, "FREE", null, 0);
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", newer));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(newer, vm.Summary);
    }

    [Fact]
    public async Task A_different_character_never_shows_the_previous_summary()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        var vm = Build();
        await vm.SignInCommand.ExecuteAsync(null);
        _dialog.OnShow = Reports(SignInResult.Connected("Other Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Other Hero", vm.CharacterName);
        Assert.Null(vm.Summary);
        Assert.False(vm.HasSummary);
    }

    // ---- Local persistence ----

    private static readonly DateTimeOffset Now = new(2030, 3, 4, 5, 6, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly FakeAccountDataPersistence _persistence = new();

    private readonly FakeAccountSessionData _session = new();

    private AccountViewModel BuildPersisted(
        Func<string, Task<EquipmentLoadResult>>? equipment = null, Func<int, Task<BadgesLoadResult>>? badges = null) =>
        new(
            _dialog,
            equipment is null ? null : new FakeEquipmentLoader(equipment),
            badges is null ? null : new FakeBadgesLoader(badges),
            _persistence,
            new FixedTimeProvider(Now),
            _session);

    private static string ExpectedUpdated(DateTimeOffset at) =>
        "Last updated " + at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static Func<string, Task<EquipmentLoadResult>> EquipsPersisted(CharacterEquipment equipment) =>
        _ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Loaded, equipment, true));

    private static Func<int, Task<BadgesLoadResult>> BadgesPersisted(CharacterBadges badges) =>
        _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Loaded, badges, true));

    private static RestoredAccount StoredAccount() =>
        new("Test Hero", SampleAccount.Summary, Earlier, Equipment("Test Hero", 3), Earlier.AddMinutes(1), Badges(3), Earlier.AddMinutes(2));

    [Fact]
    public async Task Restore_fills_name_summary_equipment_badges_and_marks_data_as_stored()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.RestoreStoredDataAsync();

        Assert.True(vm.IsConnected);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal(SampleAccount.Summary, vm.Summary);
        Assert.Equal("Test Hero", vm.Equipment!.Name);
        Assert.Equal(3, vm.Badges!.Count);
        Assert.True(vm.IsFromStoredData);
        Assert.Equal(AccountViewModel.StoredDataMessage, vm.Status);
        Assert.Equal(AccountViewModel.StoredDataNoticeText, vm.StoredDataNotice);
        Assert.True(vm.HasStoredDataNotice);
        Assert.Equal(ExpectedUpdated(Earlier), vm.SummaryUpdatedText);
        Assert.Equal(ExpectedUpdated(Earlier.AddMinutes(1)), vm.EquipmentUpdatedText);
        Assert.Equal(ExpectedUpdated(Earlier.AddMinutes(2)), vm.BadgesUpdatedText);
        Assert.True(vm.HasSummaryUpdatedText && vm.HasEquipmentUpdatedText && vm.HasBadgesUpdatedText);
        Assert.True(vm.ShowSignIn);
        Assert.False(vm.NeedsSignIn);
        Assert.Contains(nameof(AccountViewModel.ShowSignIn), changed);
        Assert.Contains(nameof(AccountViewModel.StoredDataNotice), changed);
        Assert.Contains(nameof(AccountViewModel.HasStoredDataNotice), changed);
        Assert.Contains(nameof(AccountViewModel.SummaryUpdatedText), changed);
        Assert.Contains(nameof(AccountViewModel.HasEquipmentUpdatedText), changed);
        Assert.Contains(nameof(AccountViewModel.BadgesUpdatedText), changed);
        Assert.DoesNotContain(_persistence.Calls, c => c.StartsWith("Save", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_does_nothing_when_nothing_is_stored()
    {
        var vm = BuildPersisted();

        await vm.RestoreStoredDataAsync();

        Assert.False(vm.IsConnected);
        Assert.False(vm.IsFromStoredData);
        Assert.Equal(AccountViewModel.SignedOutMessage, vm.Status);
        Assert.True(vm.ShowSignIn);
        Assert.Equal(string.Empty, vm.StoredDataNotice);
        Assert.Equal(string.Empty, vm.SummaryUpdatedText);
    }

    [Fact]
    public async Task Restore_does_not_override_a_name_that_was_set_meanwhile()
    {
        var release = new TaskCompletionSource();
        _persistence.ToRestore = StoredAccount();
        _persistence.RestoreGate = release.Task;
        var vm = BuildPersisted();

        var restoring = vm.RestoreStoredDataAsync();
        _dialog.OnShow = Reports(SignInResult.Connected("Other Hero"));
        await vm.SignInCommand.ExecuteAsync(null);
        release.SetResult();
        await restoring;

        Assert.Equal("Other Hero", vm.CharacterName);
        Assert.False(vm.IsFromStoredData);
        Assert.Null(vm.Equipment);
        Assert.Equal(AccountViewModel.ReadMessage, vm.Status);
    }

    [Fact]
    public async Task Connected_sign_in_saves_the_identity_and_summary_and_clears_the_stored_flag()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted();
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["SaveIdentity:Test Hero"], _persistence.Calls);
        Assert.Equal(("Test Hero", SampleAccount.Summary, Now), _persistence.SavedIdentities.Single());
        Assert.False(vm.IsFromStoredData);
        Assert.Equal(string.Empty, vm.StoredDataNotice);
        Assert.False(vm.HasStoredDataNotice);
        Assert.False(vm.ShowSignIn);
        Assert.Equal(ExpectedUpdated(Now), vm.SummaryUpdatedText);
        Assert.Equal(AccountViewModel.ReadMessage, vm.Status);
    }

    [Fact]
    public async Task Connected_sign_in_with_a_different_name_clears_equipment_and_badges_in_memory()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted();
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Other Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        // The stored equipment and badges are removed by the coordinator when the identity changes.
        Assert.Equal(["SaveIdentity:Other Hero"], _persistence.Calls);
        Assert.Equal("Other Hero", vm.CharacterName);
        Assert.Null(vm.Equipment);
        Assert.Null(vm.Badges);
        Assert.Equal(string.Empty, vm.EquipmentUpdatedText);
        Assert.Equal(string.Empty, vm.BadgesUpdatedText);
        Assert.Equal(ExpectedUpdated(Now), vm.SummaryUpdatedText);
    }

    [Fact]
    public async Task The_first_sign_in_does_not_remove_anything()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal(["SaveIdentity:Test Hero"], _persistence.Calls);
    }

    [Fact]
    public async Task Cancelled_or_failed_sign_in_does_not_touch_persistence()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted();
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();

        await vm.RefreshCommand.ExecuteAsync(null);
        _dialog.OnShow = Reports(SignInResult.Failed("Could not read."));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(_persistence.Calls);
        Assert.True(vm.IsFromStoredData);
        Assert.Equal("Test Hero", vm.CharacterName);
    }

    [Fact]
    public async Task Loaded_and_persisted_equipment_is_saved_with_its_read_time()
    {
        var equipment = Equipment("Test Hero", 3);
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(EquipsPersisted(equipment));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal((equipment, Now), _persistence.SavedEquipment.Single());
        Assert.Equal(ExpectedUpdated(Now), vm.EquipmentUpdatedText);
        Assert.True(vm.HasEquipmentUpdatedText);
    }

    [Fact]
    public async Task Loaded_but_not_persisted_equipment_is_shown_but_not_saved()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(Equips(Equipment("Test Hero", 3)));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.HasEquipment);
        Assert.Empty(_persistence.SavedEquipment);
        Assert.DoesNotContain("RemoveEquipment", _persistence.Calls);
        Assert.Equal(string.Empty, vm.EquipmentUpdatedText);
    }

    [Fact]
    public async Task Equipment_not_found_never_removes_the_stored_equipment()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted(_ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.NotFound)));
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["SaveIdentity:Test Hero"], _persistence.Calls);
        Assert.Null(vm.Equipment);
        Assert.Equal(string.Empty, vm.EquipmentUpdatedText);
        Assert.Equal(AccountViewModel.EquipmentNotFoundText, vm.EquipmentStatus);
    }

    [Fact]
    public async Task Unavailable_equipment_keeps_stored_equipment_untouched()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted(_ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Unavailable)));
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["SaveIdentity:Test Hero"], _persistence.Calls);
        Assert.NotNull(vm.Equipment);
        Assert.Equal(ExpectedUpdated(Earlier.AddMinutes(1)), vm.EquipmentUpdatedText);
    }

    [Fact]
    public async Task Badges_loaded_and_persisted_with_items_are_saved()
    {
        var badges = Badges(3);
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(EquipsPersisted(Equipment("Test Hero", 3)), BadgesPersisted(badges));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal((badges, Now), _persistence.SavedBadges.Single());
        Assert.Equal(ExpectedUpdated(Now), vm.BadgesUpdatedText);
        Assert.True(vm.HasBadgesUpdatedText);
    }

    [Fact]
    public async Task Loaded_but_not_persisted_badges_are_shown_but_not_saved()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(3)));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.HasBadges);
        Assert.Empty(_persistence.SavedBadges);
        Assert.DoesNotContain("RemoveBadges", _persistence.Calls);
        Assert.Equal(string.Empty, vm.BadgesUpdatedText);
    }

    [Fact]
    public async Task Persisted_empty_badges_remove_the_stored_badges()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted(EquipsPersisted(Equipment("Test Hero", 3)), BadgesPersisted(Badges(0)));
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("RemoveBadges", _persistence.Calls);
        Assert.Empty(_persistence.SavedBadges);
        Assert.Equal(string.Empty, vm.BadgesUpdatedText);
    }

    [Fact]
    public async Task Badges_not_found_never_remove_the_stored_badges()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted(
            EquipsPersisted(Equipment("Test Hero", 3)), _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.NotFound)));
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain("RemoveBadges", _persistence.Calls);
        Assert.Null(vm.Badges);
        Assert.Equal(string.Empty, vm.BadgesUpdatedText);
        Assert.Equal(AccountViewModel.BadgesNotFoundText, vm.BadgesStatus);
    }

    [Fact]
    public async Task Ok_but_not_persisted_empty_badges_do_not_remove_the_stored_badges()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted(
            EquipsPersisted(Equipment("Test Hero", 3)), _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Loaded, Badges(0), false)));
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain("RemoveBadges", _persistence.Calls);
    }

    [Fact]
    public async Task Unavailable_badges_keep_the_stored_badges_untouched()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted(
            EquipsPersisted(Equipment("Test Hero", 3)), _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Unavailable)));
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain("RemoveBadges", _persistence.Calls);
        Assert.NotNull(vm.Badges);
    }

    [Fact]
    public async Task Updated_texts_use_local_time_and_are_empty_without_a_read_time()
    {
        var vm = BuildPersisted();
        Assert.Equal(string.Empty, vm.SummaryUpdatedText);
        Assert.Equal(string.Empty, vm.EquipmentUpdatedText);
        Assert.Equal(string.Empty, vm.BadgesUpdatedText);
        Assert.False(vm.HasSummaryUpdatedText);
        Assert.False(vm.HasEquipmentUpdatedText);
        Assert.False(vm.HasBadgesUpdatedText);

        _persistence.ToRestore = StoredAccount() with { EquipmentReadAtUtc = null, Badges = null, BadgesReadAtUtc = null };
        await vm.RestoreStoredDataAsync();

        Assert.Equal("Last updated " + Earlier.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), vm.SummaryUpdatedText);
        Assert.Equal(string.Empty, vm.EquipmentUpdatedText);
        Assert.Equal(string.Empty, vm.BadgesUpdatedText);
        Assert.True(vm.HasSummaryUpdatedText);
        Assert.False(vm.HasEquipmentUpdatedText);
    }

    [Fact]
    public async Task Forget_needs_confirmation()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted();
        await vm.RestoreStoredDataAsync();
        vm.OpenBadgesDetailsCommand.Execute(null);
        vm.EquipmentStatus = "x";
        vm.BadgesStatus = "y";
        _persistence.Calls.Clear();

        vm.RequestForgetCommand.Execute(null);
        Assert.True(vm.IsForgetPending);
        Assert.DoesNotContain("Forget", _persistence.Calls);

        vm.CancelForgetCommand.Execute(null);
        Assert.False(vm.IsForgetPending);
        Assert.DoesNotContain("Forget", _persistence.Calls);
        Assert.True(vm.IsConnected);

        vm.RequestForgetCommand.Execute(null);
        vm.ConfirmForgetCommand.Execute(null);

        Assert.Equal(["Forget"], _persistence.Calls);
        Assert.False(vm.IsForgetPending);
        Assert.Null(vm.CharacterName);
        Assert.Null(vm.Summary);
        Assert.Null(vm.Equipment);
        Assert.Null(vm.Badges);
        Assert.Equal(string.Empty, vm.SummaryUpdatedText);
        Assert.Equal(string.Empty, vm.EquipmentUpdatedText);
        Assert.Equal(string.Empty, vm.BadgesUpdatedText);
        Assert.Equal(string.Empty, vm.EquipmentStatus);
        Assert.Equal(string.Empty, vm.BadgesStatus);
        Assert.False(vm.IsSidebarOpen);
        Assert.False(vm.IsFromStoredData);
        Assert.Equal(string.Empty, vm.StoredDataNotice);
        Assert.Equal(AccountViewModel.ForgottenMessage, vm.Status);
        Assert.Equal(AccountViewModel.NotConnectedText, vm.Headline);
        Assert.True(vm.ShowSignIn);
    }

    [Fact]
    public async Task Restore_seeds_the_session_data_with_what_was_restored()
    {
        var stored = StoredAccount();
        _persistence.ToRestore = stored;
        var vm = BuildPersisted();

        await vm.RestoreStoredDataAsync();

        Assert.Same(stored, Assert.Single(_session.Seeded));
    }

    [Fact]
    public async Task A_restore_that_finds_nothing_seeds_nothing()
    {
        var vm = BuildPersisted();

        await vm.RestoreStoredDataAsync();

        Assert.Empty(_session.Seeded);
    }

    [Fact]
    public async Task Forget_that_fails_keeps_everything_and_says_so()
    {
        _persistence.ToRestore = StoredAccount();
        _persistence.ForgetResult = false;
        var vm = BuildPersisted();
        await vm.RestoreStoredDataAsync();
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.False(vm.IsForgetPending);
        Assert.Equal(AccountViewModel.ForgetFailedMessage, vm.Status);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.NotNull(vm.Summary);
        Assert.NotNull(vm.Equipment);
        Assert.NotNull(vm.Badges);
        Assert.True(vm.IsFromStoredData);
        Assert.Equal(0, _session.ClearCalls);
    }

    [Fact]
    public async Task Forget_that_succeeds_clears_the_session_data_and_resets_the_sidebar_card()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted();
        await vm.RestoreStoredDataAsync();
        vm.OpenBadgesDetailsCommand.Execute(null);
        Assert.Equal(AccountViewModel.SidebarCard.Badges, vm.SidebarContent);
        vm.RequestForgetCommand.Execute(null);

        vm.ConfirmForgetCommand.Execute(null);

        Assert.Equal(1, _session.ClearCalls);
        Assert.Equal(AccountViewModel.SidebarCard.Equipped, vm.SidebarContent);
        Assert.Equal(AccountViewModel.ForgottenMessage, vm.Status);
    }

    [Fact]
    public void Forget_can_only_be_requested_while_connected()
    {
        var vm = BuildPersisted();

        vm.RequestForgetCommand.Execute(null);

        Assert.False(vm.IsForgetPending);
    }

    [Fact]
    public async Task Forget_cancels_an_in_flight_character_load()
    {
        var gate = new TaskCompletionSource<EquipmentLoadResult>();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(_ => gate.Task, BadgesPersisted(Badges(2)));

        var signingIn = vm.SignInCommand.ExecuteAsync(null);
        Assert.Equal(AccountViewModel.EquipmentLoadingText, vm.EquipmentStatus);
        vm.RequestForgetCommand.Execute(null);
        vm.ConfirmForgetCommand.Execute(null);
        gate.SetResult(new EquipmentLoadResult(EquipmentLoadStatus.Loaded, Equipment("Test Hero", 3), true));
        await signingIn;

        Assert.Null(vm.CharacterName);
        Assert.Null(vm.Equipment);
        Assert.Null(vm.Badges);
        Assert.Equal(string.Empty, vm.EquipmentStatus);
        Assert.Equal(AccountViewModel.ForgottenMessage, vm.Status);
        Assert.Empty(_persistence.SavedEquipment);
        Assert.Empty(_persistence.SavedBadges);
    }

    [Fact]
    public async Task Persistence_is_optional()
    {
        var vm = new AccountViewModel(_dialog, null, null, null, new FixedTimeProvider(Now));

        await vm.RestoreStoredDataAsync();
        Assert.False(vm.IsConnected);

        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        await vm.SignInCommand.ExecuteAsync(null);
        Assert.Equal(ExpectedUpdated(Now), vm.SummaryUpdatedText);

        vm.RequestForgetCommand.Execute(null);
        vm.ConfirmForgetCommand.Execute(null);
        Assert.Null(vm.CharacterName);
        Assert.Equal(AccountViewModel.ForgottenMessage, vm.Status);
    }

    [Fact]
    public async Task A_persistence_that_throws_never_breaks_restore()
    {
        var vm = new AccountViewModel(_dialog, persistence: new ThrowingPersistence());

        var error = await Record.ExceptionAsync(vm.RestoreStoredDataAsync);

        Assert.Null(error);
        Assert.False(vm.IsConnected);
    }

    private sealed class ThrowingPersistence : IAccountDataPersistence
    {
        public RestoredAccount? Restore() => throw new IOException("boom");

        public void SaveIdentity(string characterName, AccountSummary? summary, DateTimeOffset readAtUtc) { }

        public void SaveEquipment(CharacterEquipment equipment, DateTimeOffset readAtUtc) { }

        public void SaveBadges(CharacterBadges badges, DateTimeOffset readAtUtc) { }

        public void RemoveEquipment() { }

        public void RemoveBadges() { }

        public bool Forget() => true;
    }
}
