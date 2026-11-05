using AqwCompanion.App;
using AqwCompanion.App.SignIn;
using AqwCompanion.App.ViewModels;
using AqwCompanion.App.Views;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AqwCompanion.Tests.Desktop;

/// <summary>Layout and keyboard behaviour of the sign-in window. No browser control is ever created here.</summary>
public class SignInWindowTests
{
    private readonly FakeBrowserHost _host = new();

    private SignInViewModel BuildViewModel(SignInMode mode) =>
        new(
            _host,
            new IngestionService<AccountIdentity>(
                new AccountHomeParser(),
                new LatestResultStore<AccountIdentity>(),
                new FailedSnapshotBuffer(),
                new ListLogger<IngestionService<AccountIdentity>>()),
            new FakeDelay(),
            mode);

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Control {name} not found");

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void Full_layout_shows_the_read_only_domain_the_browser_area_and_the_note()
    {
        var vm = BuildViewModel(SignInMode.Full);
        var window = new SignInWindow(vm);

        window.Show();
        Settle(window);

        Assert.Equal(460, window.Width);
        Assert.Equal(640, window.Height);
        Assert.False(window.CanResize);
        Assert.False(window.CanMinimize);
        Assert.True(Find<Border>(window, "AddressStrip").IsVisible);
        Assert.Equal("account.aq.com", Find<TextBlock>(window, "DomainText").Text);
        Assert.False(Find<StackPanel>(window, "CompactPanel").IsVisible);
        Assert.True(Find<Border>(window, "BrowserSlot").IsVisible);
        Assert.DoesNotContain("compact", Find<Border>(window, "BrowserSlot").Classes);
        Assert.Equal(
            "This app only reads your character name after you sign in. Use the close button to cancel.",
            Find<TextBlock>(window, "PrivacyNote").Text);
        Assert.Equal(SignInViewModel.SigningInMessage, Find<TextBlock>(window, "StatusText").Text);
        Assert.Empty(window.GetVisualDescendants().OfType<NativeWebView>());
    }

    [AvaloniaFact]
    public void Compact_layout_shows_progress_and_cancel_and_keeps_the_browser_slot_one_pixel_big()
    {
        var vm = BuildViewModel(SignInMode.Compact);
        var window = new SignInWindow(vm);

        window.Show();
        Settle(window);

        Assert.Equal(360, window.Width);
        Assert.Equal(140, window.Height);
        Assert.False(Find<Border>(window, "AddressStrip").IsVisible);
        Assert.False(Find<TextBlock>(window, "PrivacyNote").IsVisible);
        Assert.True(Find<StackPanel>(window, "CompactPanel").IsVisible);
        Assert.Equal("Updating from account.aq.com...", Find<TextBlock>(window, "CompactTitle").Text);
        Assert.True(Find<ProgressBar>(window, "CompactProgress").IsIndeterminate);
        Assert.True(Find<Button>(window, "CancelButton").IsVisible);
        var slot = Find<Border>(window, "BrowserSlot");
        Assert.True(slot.IsVisible);
        Assert.Contains("compact", slot.Classes);
        Assert.Equal(1, slot.Bounds.Width);
        Assert.Equal(1, slot.Bounds.Height);
        Assert.True(slot.Bounds.X >= 0 && slot.Bounds.Y >= 0);
    }

    [AvaloniaFact]
    public void An_expired_session_grows_the_same_window_into_the_full_layout()
    {
        var vm = BuildViewModel(SignInMode.Compact);
        var window = new SignInWindow(vm);
        window.Show();
        vm.Start();

        _ = vm.HandleNavigationAsync("https://account.aq.com/Login");
        Settle(window);

        Assert.Equal(460, window.Width);
        Assert.Equal(640, window.Height);
        Assert.True(Find<Border>(window, "AddressStrip").IsVisible);
        Assert.False(Find<StackPanel>(window, "CompactPanel").IsVisible);
        Assert.DoesNotContain("compact", Find<Border>(window, "BrowserSlot").Classes);
        Assert.Equal("Your session expired. Please sign in again.", Find<TextBlock>(window, "StatusText").Text);
    }

    [AvaloniaFact]
    public void A_failed_read_offers_try_again_and_close()
    {
        var vm = BuildViewModel(SignInMode.Full);
        var window = new SignInWindow(vm);
        window.Show();
        vm.Start();
        _host.OnCapture = () => throw new InvalidOperationException("x");

        _ = vm.HandleNavigationAsync(AqwUrls.Home);
        Settle(window);

        Assert.True(Find<Button>(window, "TryAgainButton").IsVisible);
        Assert.True(Find<Button>(window, "ErrorCloseButton").IsVisible);
        Assert.StartsWith("Could not capture", Find<TextBlock>(window, "StatusText").Text, StringComparison.Ordinal);
        Assert.Same(Find<Button>(window, "TryAgainButton"), window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void A_browser_that_could_not_start_offers_only_close()
    {
        var vm = BuildViewModel(SignInMode.Full);
        var window = new SignInWindow(vm);
        window.Show();

        vm.ReportStartFailure("nope");
        Settle(window);

        Assert.True(Find<Button>(window, "ErrorCloseButton").IsVisible);
        Assert.False(Find<Button>(window, "TryAgainButton").IsVisible);
    }

    [AvaloniaFact]
    public void Success_replaces_the_browser_with_the_connected_message_and_then_closes_the_window()
    {
        var delay = new FakeDelay { Gate = new TaskCompletionSource() };
        var vm = new SignInViewModel(
            _host,
            new IngestionService<AccountIdentity>(
                new AccountHomeParser(),
                new LatestResultStore<AccountIdentity>(),
                new FailedSnapshotBuffer(),
                new ListLogger<IngestionService<AccountIdentity>>()),
            delay,
            SignInMode.Full);
        var window = new SignInWindow(vm);
        window.Show();
        vm.Start();
        _host.OnCapture = () => PageSnapshot.Create(
            AqwUrls.Home,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "account-home-sample.html")),
            "1.0.0",
            DateTimeOffset.UnixEpoch);

        _ = vm.HandleNavigationAsync(AqwUrls.Home);
        Settle(window);

        Assert.True(Find<StackPanel>(window, "SuccessPanel").IsVisible);
        Assert.False(Find<Border>(window, "BrowserSlot").IsVisible);
        Assert.False(Find<StackPanel>(window, "StatusPanel").IsVisible);
        Assert.True(window.IsVisible);

        delay.Gate!.SetResult();
        Settle(window);

        Assert.False(window.IsVisible);
        Assert.Equal(SignInResult.Connected("Test Hero"), vm.Result);
    }

    [AvaloniaFact]
    public void Escape_cancels_and_closes_the_window()
    {
        var vm = BuildViewModel(SignInMode.Full);
        var window = new SignInWindow(vm);
        window.Show();
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);

        Assert.Equal(SignInOutcome.Cancelled, vm.Result!.Outcome);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void Closing_the_window_any_other_way_still_cancels_the_flow()
    {
        var vm = BuildViewModel(SignInMode.Compact);
        var window = new SignInWindow(vm);
        window.Show();
        Settle(window);

        window.Close();
        Settle(window);

        Assert.Equal(SignInOutcome.Cancelled, vm.Result!.Outcome);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void Initial_focus_goes_to_a_control_inside_the_popup()
    {
        var full = new SignInWindow(BuildViewModel(SignInMode.Full));
        full.Show();
        Settle(full);
        Assert.Same(Find<Button>(full, "CloseButton"), full.FocusManager!.GetFocusedElement());

        var compact = new SignInWindow(BuildViewModel(SignInMode.Compact));
        compact.Show();
        Settle(compact);
        Assert.Same(Find<Button>(compact, "CancelButton"), compact.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void The_window_and_every_button_have_accessible_names()
    {
        var window = new SignInWindow(BuildViewModel(SignInMode.Full));
        window.Show();
        Settle(window);

        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(window)));
        foreach (var name in new[] { "CloseButton", "TryAgainButton", "ErrorCloseButton", "CancelButton" })
        {
            Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(Find<Button>(window, name))),
                $"{name} has no automation name");
        }
    }

    [AvaloniaFact]
    public void The_window_can_hold_and_release_a_browser_control_slot()
    {
        var window = new SignInWindow(BuildViewModel(SignInMode.Full));
        var placeholder = new Border();

        window.SetBrowser(placeholder);
        Assert.Same(placeholder, Find<Border>(window, "BrowserSlot").Child);

        window.SetBrowser(null);
        Assert.Null(Find<Border>(window, "BrowserSlot").Child);
    }
}
