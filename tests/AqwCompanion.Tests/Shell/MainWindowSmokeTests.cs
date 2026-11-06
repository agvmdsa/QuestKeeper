using AqwCompanion.App;
using AqwCompanion.App.Browser;
using AqwCompanion.App.Persistence;
using AqwCompanion.App.ViewModels;
using AqwCompanion.App.Views;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Shell;

public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void Main_window_opens_with_product_title_and_account_as_the_first_of_four_tabs()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal(AppInfo.ProductName, window.Title);
        var tabs = window.FindControl<TabControl>("Tabs")!;
        Assert.Equal(4, tabs.ItemCount);
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
        Assert.Empty(account.GetVisualDescendants().OfType<NativeWebView>());
        Assert.False(services.GetRequiredService<WebViewBrowserHost>().IsAttached);
    }

    [AvaloniaFact]
    public async Task Account_view_shows_the_stored_data_notice_and_the_forget_button_after_a_restore()
    {
        var at = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        var persistence = new FakeAccountDataPersistence
        {
            ToRestore = new RestoredAccount("Test Hero", SampleAccount.Summary, at, null, null, null, null),
        };
        var vm = new AccountViewModel(new FakeSignInDialog(), persistence: persistence);
        var view = new AccountView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        await vm.RestoreStoredDataAsync();

        var notice = view.FindControl<TextBlock>("StoredDataNoticeLine")!;
        Assert.True(notice.IsVisible);
        Assert.Equal(AccountViewModel.StoredDataNoticeText, notice.Text);
        var forget = view.FindControl<Button>("ForgetButton")!;
        Assert.True(forget.IsVisible);
        Assert.Equal("Forget stored data", forget.Content);
        Assert.True(view.FindControl<Button>("SignInButton")!.IsVisible);
        Assert.True(view.FindControl<TextBlock>("SummaryUpdatedLine")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("ForgetConfirmation")!.IsVisible);

        vm.RequestForgetCommand.Execute(null);

        Assert.False(forget.IsVisible);
        Assert.True(view.FindControl<StackPanel>("ForgetConfirmation")!.IsVisible);
    }
}
