using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.SignIn;
using AqwCompanion.App.ViewModels;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Desktop;

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
}
