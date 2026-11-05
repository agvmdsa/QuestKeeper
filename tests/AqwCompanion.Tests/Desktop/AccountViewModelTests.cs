using AqwCompanion.App.SignIn;
using AqwCompanion.App.ViewModels;
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
}
