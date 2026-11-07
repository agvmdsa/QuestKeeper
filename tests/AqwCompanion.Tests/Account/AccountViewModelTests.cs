using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Account;

public class AccountViewModelTests
{
    private readonly FakeSignInDialog _dialog = new();

    private AccountViewModel Build() => AccountViewModelBuilder.Create(_dialog);

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
        var vm = AccountViewModelBuilder.Create(_dialog, new FakeEquipmentLoader(equipment), new FakeBadgesLoader(badges));
        await vm.SignInCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Loads_the_badges_for_the_character_id_after_the_equipment()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(7)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))), badgesLoader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal([3], badgesLoader.Requested);
        Assert.True(vm.Badges.HasData);
        Assert.Equal("7 badges", vm.Badges.CountText);
        Assert.Equal(5, vm.Badges.Preview.Count());
        Assert.Equal("Test Badge 1", vm.Badges.Preview.First().Title);
        Assert.Equal(["Quests", "Events"], vm.Badges.Groups.Select(g => g.Category));
        Assert.Equal(string.Empty, vm.Badges.Status);
        Assert.False(vm.Badges.HasStatus);
        Assert.True(vm.Badges.OpenDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Shows_the_loading_text_while_the_badges_are_read()
    {
        var gate = new TaskCompletionSource<BadgesLoadResult>();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(
            _dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))), new FakeBadgesLoader(_ => gate.Task));

        var signIn = vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal(BadgesCardViewModel.LoadingText, vm.Badges.Status);
        Assert.True(vm.Badges.HasStatus);
        Assert.False(vm.Badges.HasData);
        gate.SetResult(new BadgesLoadResult(BadgesLoadStatus.Loaded, Badges(2)));
        await signIn;
        Assert.Equal(string.Empty, vm.Badges.Status);
        Assert.True(vm.Badges.HasData);
    }

    [Fact]
    public async Task An_empty_badge_list_shows_the_none_found_text_and_cannot_open_the_sidebar()
    {
        var vm = await ConnectedAsync(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(0)));

        Assert.False(vm.Badges.HasData);
        Assert.Equal(BadgesCardViewModel.NotFoundText, vm.Badges.Status);
        Assert.False(vm.Badges.OpenDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Badges_that_cannot_be_read_show_the_none_found_text()
    {
        var vm = await ConnectedAsync(
            Equips(Equipment("Test Hero", 3)), _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.NotFound)));

        Assert.False(vm.Badges.HasData);
        Assert.Equal(BadgesCardViewModel.NotFoundText, vm.Badges.Status);
    }

    [Fact]
    public async Task Badges_that_cannot_be_fetched_show_the_unavailable_text()
    {
        var vm = await ConnectedAsync(
            Equips(Equipment("Test Hero", 3)), _ => Task.FromResult(new BadgesLoadResult(BadgesLoadStatus.Unavailable)));

        Assert.False(vm.Badges.HasData);
        Assert.Equal(BadgesCardViewModel.UnavailableText, vm.Badges.Status);
    }

    [Fact]
    public async Task Without_a_character_id_the_badges_are_not_requested()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", null))), badgesLoader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Empty(badgesLoader.Requested);
        Assert.True(vm.Equipment.HasData);
        Assert.Equal(BadgesCardViewModel.UnavailableText, vm.Badges.Status);
    }

    [Fact]
    public async Task When_the_equipment_is_not_loaded_the_badges_are_not_requested()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(
            _dialog,
            new FakeEquipmentLoader(_ => Task.FromResult(new EquipmentLoadResult(EquipmentLoadStatus.Unavailable))),
            badgesLoader);

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Empty(badgesLoader.Requested);
        Assert.Equal(BadgesCardViewModel.UnavailableText, vm.Badges.Status);
    }

    [Fact]
    public async Task Badges_are_requested_only_by_sign_in_and_refresh()
    {
        var badgesLoader = new FakeBadgesLoader(HasBadges(Badges(3)));
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))), badgesLoader);
        await vm.SignInCommand.ExecuteAsync(null);

        vm.Badges.OpenDetailsCommand.Execute(null);
        vm.Sidebar.CloseCommand.Execute(null);
        Assert.Single(badgesLoader.Requested);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal([3, 3], badgesLoader.Requested);
    }

    [Fact]
    public async Task A_different_character_clears_the_previous_badges()
    {
        var vm = await ConnectedAsync(
            name => Task.FromResult(name == "Test Hero"
                ? new EquipmentLoadResult(EquipmentLoadStatus.Loaded, Equipment(name, 3))
                : new EquipmentLoadResult(EquipmentLoadStatus.Unavailable)),
            HasBadges(Badges(3)));
        vm.Badges.OpenDetailsCommand.Execute(null);
        _dialog.OnShow = Reports(SignInResult.Connected("Other Hero"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.Sidebar.IsOpen);
        Assert.False(vm.Badges.HasData);
    }

    [Fact]
    public async Task Without_a_badges_loader_the_badges_card_stays_empty()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = AccountViewModelBuilder.Create(_dialog, new FakeEquipmentLoader(Equips(Equipment("Test Hero", 3))));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.Equipment.HasData);
        Assert.False(vm.Badges.HasData);
        Assert.Equal(string.Empty, vm.Badges.Status);
    }

    [Fact]
    public async Task Sign_in_shows_the_summary_with_thousands_separators()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.Summary.HasData);
        Assert.Equal("1,234,567", vm.Summary.AdventureCoinsText);
        Assert.Equal("LEGEND", vm.Summary.AccountTypeText);
        Assert.Equal("Membership Expired: Tuesday, January 2, 2030", vm.Summary.MembershipText);
        Assert.Equal("7", vm.Summary.GearsOfDoomText);
    }

    [Fact]
    public async Task A_connected_result_without_a_summary_leaves_the_card_unavailable()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = Build();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.False(vm.Summary.HasData);
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

        Assert.Equal(SampleAccount.Summary, vm.Summary.Data);
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

        Assert.Equal(newer, vm.Summary.Data);
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
        Assert.Null(vm.Summary.Data);
        Assert.False(vm.Summary.HasData);
    }

    // ---- Local persistence ----

    private static readonly DateTimeOffset Now = new(2030, 3, 4, 5, 6, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly FakeAccountDataPersistence _persistence = new();

    private readonly FakeAccountSessionData _session = new();

    private AccountViewModel BuildPersisted(
        Func<string, Task<EquipmentLoadResult>>? equipment = null, Func<int, Task<BadgesLoadResult>>? badges = null) =>
        AccountViewModelBuilder.Create(
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
        var summaryChanged = new List<string?>();
        vm.Summary.PropertyChanged += (_, e) => summaryChanged.Add(e.PropertyName);
        var equipmentChanged = new List<string?>();
        vm.Equipment.PropertyChanged += (_, e) => equipmentChanged.Add(e.PropertyName);
        var badgesChanged = new List<string?>();
        vm.Badges.PropertyChanged += (_, e) => badgesChanged.Add(e.PropertyName);

        await vm.RestoreStoredDataAsync();

        Assert.True(vm.IsConnected);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.Equal(SampleAccount.Summary, vm.Summary.Data);
        Assert.Equal("Test Hero", vm.Equipment.Data!.Name);
        Assert.Equal(3, vm.Badges.Data!.Count);
        Assert.True(vm.IsFromStoredData);
        Assert.Equal(AccountViewModel.StoredDataMessage, vm.Status);
        Assert.Equal(AccountViewModel.StoredDataNoticeText, vm.StoredDataNotice);
        Assert.True(vm.HasStoredDataNotice);
        Assert.Equal(ExpectedUpdated(Earlier), vm.Summary.UpdatedText);
        Assert.Equal(ExpectedUpdated(Earlier.AddMinutes(1)), vm.Equipment.UpdatedText);
        Assert.Equal(ExpectedUpdated(Earlier.AddMinutes(2)), vm.Badges.UpdatedText);
        Assert.True(vm.Summary.HasUpdatedText && vm.Equipment.HasUpdatedText && vm.Badges.HasUpdatedText);
        Assert.False(vm.ShowSignIn);
        Assert.False(vm.NeedsSignIn);
        Assert.Contains(nameof(AccountViewModel.ShowSignIn), changed);
        Assert.Contains(nameof(AccountViewModel.StoredDataNotice), changed);
        Assert.Contains(nameof(AccountViewModel.HasStoredDataNotice), changed);
        Assert.Contains(nameof(SummaryCardViewModel.UpdatedText), summaryChanged);
        Assert.Contains(nameof(EquipmentCardViewModel.HasUpdatedText), equipmentChanged);
        Assert.Contains(nameof(BadgesCardViewModel.UpdatedText), badgesChanged);
        Assert.Contains(nameof(AccountViewModel.ConnectionSummary), changed);
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
        Assert.Equal(string.Empty, vm.Summary.UpdatedText);
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
        Assert.Null(vm.Equipment.Data);
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
        Assert.Equal(ExpectedUpdated(Now), vm.Summary.UpdatedText);
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
        Assert.Null(vm.Equipment.Data);
        Assert.Null(vm.Badges.Data);
        Assert.Equal(string.Empty, vm.Equipment.UpdatedText);
        Assert.Equal(string.Empty, vm.Badges.UpdatedText);
        Assert.Equal(ExpectedUpdated(Now), vm.Summary.UpdatedText);
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
        Assert.Equal(ExpectedUpdated(Now), vm.Equipment.UpdatedText);
        Assert.True(vm.Equipment.HasUpdatedText);
    }

    [Fact]
    public async Task Loaded_but_not_persisted_equipment_is_shown_but_not_saved()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(Equips(Equipment("Test Hero", 3)));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.Equipment.HasData);
        Assert.Empty(_persistence.SavedEquipment);
        Assert.DoesNotContain("RemoveEquipment", _persistence.Calls);
        Assert.Equal(string.Empty, vm.Equipment.UpdatedText);
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
        Assert.Null(vm.Equipment.Data);
        Assert.Equal(string.Empty, vm.Equipment.UpdatedText);
        Assert.Equal(EquipmentCardViewModel.NotFoundText, vm.Equipment.Status);
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
        Assert.NotNull(vm.Equipment.Data);
        Assert.Equal(ExpectedUpdated(Earlier.AddMinutes(1)), vm.Equipment.UpdatedText);
    }

    [Fact]
    public async Task Badges_loaded_and_persisted_with_items_are_saved()
    {
        var badges = Badges(3);
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(EquipsPersisted(Equipment("Test Hero", 3)), BadgesPersisted(badges));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal((badges, Now), _persistence.SavedBadges.Single());
        Assert.Equal(ExpectedUpdated(Now), vm.Badges.UpdatedText);
        Assert.True(vm.Badges.HasUpdatedText);
    }

    [Fact]
    public async Task Loaded_but_not_persisted_badges_are_shown_but_not_saved()
    {
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(Equips(Equipment("Test Hero", 3)), HasBadges(Badges(3)));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.Badges.HasData);
        Assert.Empty(_persistence.SavedBadges);
        Assert.DoesNotContain("RemoveBadges", _persistence.Calls);
        Assert.Equal(string.Empty, vm.Badges.UpdatedText);
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
        Assert.Equal(string.Empty, vm.Badges.UpdatedText);
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
        Assert.Null(vm.Badges.Data);
        Assert.Equal(string.Empty, vm.Badges.UpdatedText);
        Assert.Equal(BadgesCardViewModel.NotFoundText, vm.Badges.Status);
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
        Assert.NotNull(vm.Badges.Data);
    }

    [Fact]
    public async Task Updated_texts_use_local_time_and_are_empty_without_a_read_time()
    {
        var vm = BuildPersisted();
        Assert.Equal(string.Empty, vm.Summary.UpdatedText);
        Assert.Equal(string.Empty, vm.Equipment.UpdatedText);
        Assert.Equal(string.Empty, vm.Badges.UpdatedText);
        Assert.False(vm.Summary.HasUpdatedText);
        Assert.False(vm.Equipment.HasUpdatedText);
        Assert.False(vm.Badges.HasUpdatedText);

        _persistence.ToRestore = StoredAccount() with { EquipmentReadAtUtc = null, Badges = null, BadgesReadAtUtc = null };
        await vm.RestoreStoredDataAsync();

        Assert.Equal("Last updated " + Earlier.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), vm.Summary.UpdatedText);
        Assert.Equal(string.Empty, vm.Equipment.UpdatedText);
        Assert.Equal(string.Empty, vm.Badges.UpdatedText);
        Assert.True(vm.Summary.HasUpdatedText);
        Assert.False(vm.Equipment.HasUpdatedText);
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

    private async Task<AccountViewModel> RestoredAsync()
    {
        _persistence.ToRestore = StoredAccount();
        var vm = BuildPersisted();
        await vm.RestoreStoredDataAsync();
        _persistence.Calls.Clear();
        return vm;
    }

    private static async Task SignOutAsync(AccountViewModel vm)
    {
        vm.RequestSignOutCommand.Execute(null);
        await vm.ConfirmSignOutCommand.ExecuteAsync(null);
    }

    private void AssertEverythingRemoved(AccountViewModel vm)
    {
        Assert.Equal(["Forget"], _persistence.Calls);
        Assert.False(vm.IsSignOutPending);
        Assert.Null(vm.CharacterName);
        Assert.Null(vm.Summary.Data);
        Assert.Null(vm.Equipment.Data);
        Assert.Null(vm.Badges.Data);
        Assert.Equal(string.Empty, vm.Summary.UpdatedText);
        Assert.Equal(string.Empty, vm.Equipment.UpdatedText);
        Assert.Equal(string.Empty, vm.Badges.UpdatedText);
        Assert.Equal(string.Empty, vm.Equipment.Status);
        Assert.Equal(string.Empty, vm.Badges.Status);
        Assert.False(vm.Sidebar.IsOpen);
        Assert.False(vm.IsFromStoredData);
        Assert.Equal(string.Empty, vm.StoredDataNotice);
        Assert.Equal(NotConnected, vm.Headline);
        Assert.True(vm.ShowSignIn);
        Assert.False(vm.HasLastSync);
        Assert.Equal(string.Empty, vm.LastSyncText);
        Assert.Equal(1, _session.ClearCalls);
    }

    private const string NotConnected = AccountViewModel.NotConnectedText;

    [Fact]
    public async Task Sign_out_needs_confirmation_and_opens_the_sign_out_window_only_after_it()
    {
        var vm = await RestoredAsync();
        vm.Badges.OpenDetailsCommand.Execute(null);
        vm.Equipment.Status = "x";
        vm.Badges.Status = "y";
        _dialog.OnShow = Reports(SignInResult.SignedOut());

        vm.RequestSignOutCommand.Execute(null);
        Assert.True(vm.IsSignOutPending);
        Assert.Empty(_dialog.Shown);
        Assert.DoesNotContain("Forget", _persistence.Calls);

        vm.CancelSignOutCommand.Execute(null);
        Assert.False(vm.IsSignOutPending);
        Assert.Empty(_dialog.Shown);
        Assert.True(vm.IsConnected);

        await SignOutAsync(vm);

        Assert.Equal([SignInMode.SignOut], _dialog.Shown);
        AssertEverythingRemoved(vm);
        Assert.Equal(AccountViewModel.SignOutCompleteMessage, vm.Status);
        Assert.Null(vm.Sidebar.Content);
        Assert.Equal(string.Empty, vm.Sidebar.Title);
    }

    [Fact]
    public async Task Sign_out_that_could_not_reach_the_site_still_removes_the_local_data_and_says_so()
    {
        var vm = await RestoredAsync();
        _dialog.OnShow = Reports(SignInResult.Failed("Could not reach account.aq.com."));

        await SignOutAsync(vm);

        AssertEverythingRemoved(vm);
        Assert.Equal(AccountViewModel.SignOutUnreachableMessage, vm.Status);
    }

    [Fact]
    public async Task Sign_out_whose_window_throws_is_treated_as_unreachable()
    {
        var vm = await RestoredAsync();
        _dialog.OnShow = _ => throw new InvalidOperationException("boom");

        await SignOutAsync(vm);

        AssertEverythingRemoved(vm);
        Assert.Equal(AccountViewModel.SignOutUnreachableMessage, vm.Status);
    }

    [Fact]
    public async Task Sign_out_dismissed_changes_nothing()
    {
        var vm = await RestoredAsync();
        _dialog.OnShow = Reports(SignInResult.Cancelled());

        await SignOutAsync(vm);

        Assert.Empty(_persistence.Calls);
        Assert.False(vm.IsSignOutPending);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.NotNull(vm.Summary.Data);
        Assert.NotNull(vm.Equipment.Data);
        Assert.NotNull(vm.Badges.Data);
        Assert.True(vm.IsFromStoredData);
        Assert.Equal(AccountViewModel.CancelledMessage, vm.Status);
        Assert.Equal(0, _session.ClearCalls);
        Assert.True(vm.HasLastSync);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sign_out_that_cannot_remove_the_local_data_keeps_everything_and_says_so(bool signedOut)
    {
        _persistence.ForgetResult = false;
        var vm = await RestoredAsync();
        _dialog.OnShow = Reports(signedOut ? SignInResult.SignedOut() : SignInResult.Failed("x"));

        await SignOutAsync(vm);

        Assert.False(vm.IsSignOutPending);
        Assert.Equal(AccountViewModel.ForgetFailedMessage, vm.Status);
        Assert.Equal("Test Hero", vm.CharacterName);
        Assert.NotNull(vm.Summary.Data);
        Assert.NotNull(vm.Equipment.Data);
        Assert.NotNull(vm.Badges.Data);
        Assert.True(vm.IsFromStoredData);
        Assert.Equal(0, _session.ClearCalls);
        Assert.True(vm.HasLastSync);
    }

    [Fact]
    public void Sign_out_can_only_be_requested_while_connected()
    {
        var vm = BuildPersisted();

        Assert.False(vm.RequestSignOutCommand.CanExecute(null));
        vm.RequestSignOutCommand.Execute(null);

        Assert.False(vm.IsSignOutPending);
    }

    [Fact]
    public async Task Confirming_sign_out_while_a_window_is_open_does_nothing()
    {
        var vm = await RestoredAsync();
        var gate = new TaskCompletionSource<SignInResult>();
        _dialog.OnShow = _ => gate.Task;
        var refreshing = vm.RefreshCommand.ExecuteAsync(null);
        vm.RequestSignOutCommand.Execute(null);

        await vm.ConfirmSignOutCommand.ExecuteAsync(null);

        Assert.False(vm.IsSignOutPending);
        Assert.Equal([SignInMode.Compact], _dialog.Shown);
        Assert.DoesNotContain("Forget", _persistence.Calls);
        gate.SetResult(SignInResult.Cancelled());
        await refreshing;
    }

    [Fact]
    public async Task Sign_out_cancels_an_in_flight_character_load()
    {
        var gate = new TaskCompletionSource<EquipmentLoadResult>();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        var vm = BuildPersisted(_ => gate.Task, BadgesPersisted(Badges(2)));

        var signingIn = vm.SignInCommand.ExecuteAsync(null);
        Assert.Equal(EquipmentCardViewModel.LoadingText, vm.Equipment.Status);
        _dialog.OnShow = Reports(SignInResult.SignedOut());
        await SignOutAsync(vm);
        gate.SetResult(new EquipmentLoadResult(EquipmentLoadStatus.Loaded, Equipment("Test Hero", 3), true));
        await signingIn;

        Assert.Null(vm.CharacterName);
        Assert.Null(vm.Equipment.Data);
        Assert.Null(vm.Badges.Data);
        Assert.Equal(string.Empty, vm.Equipment.Status);
        Assert.Equal(AccountViewModel.SignOutCompleteMessage, vm.Status);
        Assert.Empty(_persistence.SavedEquipment);
        Assert.Empty(_persistence.SavedBadges);
    }

    [Fact]
    public void Sign_out_texts_are_honest()
    {
        Assert.Equal("Sign out of account.aq.com and remove everything the app saved on this computer?", AccountViewModel.SignOutPromptText);
        Assert.Equal("Signed out. The saved data was removed from this computer.", AccountViewModel.SignOutCompleteMessage);
        Assert.Equal(
            "The saved data was removed, but account.aq.com could not be reached, so your site session may still be active.",
            AccountViewModel.SignOutUnreachableMessage);
    }

    [Fact]
    public async Task Headline_says_saved_data_until_the_session_is_read_this_run()
    {
        var vm = await RestoredAsync();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("Saved data for Test Hero", vm.Headline);

        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Connected as Test Hero", vm.Headline);
        Assert.Contains(nameof(AccountViewModel.Headline), changed);
    }

    [Fact]
    public async Task Sign_in_is_offered_only_while_not_connected()
    {
        var vm = BuildPersisted();
        Assert.True(vm.ShowSignIn);

        _persistence.ToRestore = StoredAccount();
        await vm.RestoreStoredDataAsync();
        Assert.False(vm.ShowSignIn);
        Assert.True(vm.IsFromStoredData);
    }

    [Fact]
    public async Task Last_sync_follows_the_identity_read_time()
    {
        var vm = BuildPersisted();
        Assert.False(vm.HasLastSync);
        Assert.Equal(string.Empty, vm.LastSyncText);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _persistence.ToRestore = StoredAccount();
        await vm.RestoreStoredDataAsync();
        Assert.True(vm.HasLastSync);
        Assert.Equal("Last sync: " + Earlier.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), vm.LastSyncText);
        Assert.Contains(nameof(AccountViewModel.LastSyncText), changed);

        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Last sync: " + Now.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), vm.LastSyncText);

        _dialog.OnShow = Reports(SignInResult.SignedOut());
        await SignOutAsync(vm);
        Assert.False(vm.HasLastSync);
        Assert.Equal(string.Empty, vm.LastSyncText);
    }

    [Fact]
    public async Task Last_sync_is_set_after_a_first_sign_in()
    {
        var vm = BuildPersisted();
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.True(vm.HasLastSync);
        Assert.Equal("Last sync: " + Now.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), vm.LastSyncText);
    }

    [Fact]
    public async Task Persistence_is_optional()
    {
        var vm = AccountViewModelBuilder.Create(_dialog, null, null, null, new FixedTimeProvider(Now));

        await vm.RestoreStoredDataAsync();
        Assert.False(vm.IsConnected);

        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero", SampleAccount.Summary));
        await vm.SignInCommand.ExecuteAsync(null);
        Assert.Equal(ExpectedUpdated(Now), vm.Summary.UpdatedText);

        _dialog.OnShow = Reports(SignInResult.SignedOut());
        vm.RequestSignOutCommand.Execute(null);
        await vm.ConfirmSignOutCommand.ExecuteAsync(null);
        Assert.Null(vm.CharacterName);
        Assert.Equal(AccountViewModel.SignOutCompleteMessage, vm.Status);
    }

    [Fact]
    public async Task A_persistence_that_throws_never_breaks_restore()
    {
        var vm = AccountViewModelBuilder.Create(_dialog, persistence: new ThrowingPersistence());

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

        public void SaveInventory(CharacterInventory inventory, DateTimeOffset readAtUtc) { }

        public void RemoveInventory() { }

        public bool Forget() => true;
    }

    /// <summary>A connected view model whose coordinator also orders a recording participant and the persistence removal.</summary>
    private async Task<(AccountViewModel Vm, List<string> Log, RecordingForgettable Other)> ConnectedWithParticipantAsync()
    {
        var log = new List<string>();
        var other = new RecordingForgettable("Other", log);
        AccountViewModel? vm = null;
        var coordinator = new ForgetCoordinator(
            () =>
            {
                log.Add("RemoveStoredData");
                return _persistence.Forget();
            },
            () => [other, (IForgettable)vm!]);
        _dialog.OnShow = Reports(SignInResult.Connected("Test Hero"));
        vm = AccountViewModelBuilder.Create(_dialog, persistence: _persistence, forgetting: coordinator);
        await vm.SignInCommand.ExecuteAsync(null);
        return (vm, log, other);
    }

    [Fact]
    public async Task A_completed_sign_out_stops_work_then_removes_the_data_then_forgets()
    {
        var (vm, log, _) = await ConnectedWithParticipantAsync();
        _dialog.OnShow = Reports(SignInResult.SignedOut());

        await SignOutAsync(vm);

        Assert.Equal(["Quiesce:Other", "RemoveStoredData", "Forget:Other"], log);
        Assert.Null(vm.CharacterName);
        Assert.Equal(AccountViewModel.SignOutCompleteMessage, vm.Status);
    }

    [Fact]
    public async Task The_other_features_are_forgotten_when_the_site_was_unreachable_but_the_data_was_removed()
    {
        var (vm, log, _) = await ConnectedWithParticipantAsync();
        _dialog.OnShow = Reports(SignInResult.Failed("Could not reach account.aq.com."));

        await SignOutAsync(vm);

        Assert.Equal(["Quiesce:Other", "RemoveStoredData", "Forget:Other"], log);
        Assert.Null(vm.CharacterName);
    }

    [Fact]
    public async Task Nothing_is_forgotten_when_the_sign_out_is_dismissed()
    {
        var (vm, log, _) = await ConnectedWithParticipantAsync();
        _dialog.OnShow = Reports(SignInResult.Cancelled());

        await SignOutAsync(vm);

        Assert.Empty(log);
        Assert.Equal("Test Hero", vm.CharacterName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task When_the_stored_data_could_not_be_removed_nothing_is_cleared_and_the_participants_resume(bool signedOut)
    {
        var (vm, log, _) = await ConnectedWithParticipantAsync();
        _persistence.ForgetResult = false;
        _dialog.OnShow = Reports(signedOut ? SignInResult.SignedOut() : SignInResult.Failed("x"));

        await SignOutAsync(vm);

        Assert.Equal(["Quiesce:Other", "RemoveStoredData", "Resume:Other"], log);
        Assert.Equal(AccountViewModel.ForgetFailedMessage, vm.Status);
        Assert.Equal("Test Hero", vm.CharacterName);
    }

    [Fact]
    public async Task A_feature_that_could_not_be_forgotten_is_named_in_the_status_and_the_account_is_still_cleared()
    {
        var (vm, log, other) = await ConnectedWithParticipantAsync();
        other.ThrowOnForget = true;
        _dialog.OnShow = Reports(SignInResult.SignedOut());

        await SignOutAsync(vm);

        Assert.Equal(AccountViewModel.SignOutCompleteMessage + " Some data could not be removed: Other.", vm.Status);
        Assert.Null(vm.CharacterName);
        Assert.Contains("Forget:Other", log);
    }
}
