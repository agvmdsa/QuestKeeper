using AqwCompanion.App;
using AqwCompanion.App.Account;
using AqwCompanion.App.Infrastructure.Browser;
using AqwCompanion.App.Shell;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Shell;

public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void Main_window_opens_with_product_title_and_account_as_the_first_of_six_tabs()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal(AppInfo.ProductName, window.Title);
        var tabs = window.FindControl<TabControl>("Tabs")!;
        Assert.Equal(6, tabs.ItemCount);
        Assert.Equal("Account", Assert.IsType<TabItem>(tabs.Items[0]).Header);
        Assert.Empty(window.GetVisualDescendants().OfType<NativeWebView>());
    }

    [AvaloniaFact]
    public void Account_tab_is_a_card_with_a_sign_in_button_and_no_browser()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        var account = window.GetVisualDescendants().OfType<AccountView>().Single();
        Assert.Equal("Not connected", account.FindControl<TextBlock>("Headline")!.Text);
        Assert.True(account.FindControl<Button>("SignInButton")!.IsVisible);
        Assert.False(account.FindControl<Button>("RefreshButton")!.IsVisible);
        Assert.False(account.FindControl<Button>("SignOutButton")!.IsEffectivelyVisible);
        Assert.False(account.FindControl<TextBlock>("LastSyncLine")!.IsVisible);
        Assert.Empty(account.GetVisualDescendants().OfType<NativeWebView>());
        Assert.False(services.GetRequiredService<WebViewBrowserHost>().IsAttached);
    }

    [AvaloniaFact]
    public async Task Account_view_shows_the_saved_session_honestly_with_sign_out_and_the_last_sync_after_a_restore()
    {
        var at = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        var persistence = new FakeAccountDataPersistence
        {
            ToRestore = new RestoredAccount("Test Hero", SampleAccount.Summary, at, null, null, null, null),
        };
        var vm = AccountViewModelBuilder.Create(new FakeSignInDialog(), persistence: persistence);
        var view = new AccountView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        await vm.RestoreStoredDataAsync();
        window.UpdateLayout();

        Assert.Equal("Saved data for Test Hero", view.FindControl<TextBlock>("Headline")!.Text);
        var lastSync = view.FindControl<TextBlock>("LastSyncLine")!;
        Assert.True(lastSync.IsVisible);
        Assert.Equal(vm.LastSyncText, lastSync.Text);
        Assert.StartsWith("Last sync: ", lastSync.Text);
        var notice = view.FindControl<TextBlock>("StoredDataNoticeLine")!;
        Assert.True(notice.IsVisible);
        Assert.Equal(AccountViewModel.StoredDataNoticeText, notice.Text);
        Assert.False(view.FindControl<Button>("SignInButton")!.IsVisible);
        Assert.True(view.FindControl<Button>("RefreshButton")!.IsVisible);
        var signOut = view.FindControl<Button>("SignOutButton")!;
        Assert.True(signOut.IsVisible);
        Assert.Equal("Sign out", signOut.Content);
        Assert.True(view.Named<TextBlock>("SummaryUpdatedLine").IsVisible);
        var confirmation = view.FindControl<StackPanel>("SignOutConfirmation")!;
        Assert.False(confirmation.IsVisible);

        vm.RequestSignOutCommand.Execute(null);

        Assert.False(signOut.IsVisible);
        Assert.True(confirmation.IsVisible);
        Assert.Equal(AccountViewModel.SignOutPromptText, view.FindControl<TextBlock>("SignOutPrompt")!.Text);
        Assert.Equal("Sign out", view.FindControl<Button>("ConfirmSignOutButton")!.Content);
        Assert.Equal("Keep", view.FindControl<Button>("CancelSignOutButton")!.Content);

        vm.CancelSignOutCommand.Execute(null);

        Assert.True(signOut.IsVisible);
        Assert.False(confirmation.IsVisible);
    }
}
