using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Infrastructure.Browser;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Ingestion;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Serilog;

namespace AqwCompanion.App.SignIn;

/// <summary>
/// Shows <see cref="SignInWindow"/> as a modal dialog over the main window. The native browser control is
/// created when the window opens and released when it closes; nothing here runs at application start.
/// </summary>
public sealed class SignInDialog(
    WebViewBrowserHost browser,
    IngestionService<AccountIdentity> ingestion,
    IDelay delay) : ISignInDialog
{
    public async Task<SignInResult> ShowAsync(SignInMode mode)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            {
                return SignInResult.Failed("The main window is not available.");
            }

            var returnFocus = TopLevel.GetTopLevel(owner)?.FocusManager?.GetFocusedElement();
            using var viewModel = new SignInViewModel(browser, ingestion, delay, mode);
            var window = new SignInWindow(viewModel);
            window.Opened += (_, _) => StartBrowser(window, viewModel);
            window.Closed += (_, _) =>
            {
                window.SetBrowser(null);
                browser.Detach();
            };

            try
            {
                await window.ShowDialog(owner);
            }
            finally
            {
                // Hand focus back to the button that opened the window.
                (returnFocus as InputElement ?? owner).Focus();
            }

            return viewModel.Result ?? SignInResult.Cancelled();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error(ex, "The sign-in window failed");
            return SignInResult.Failed($"Could not open the sign-in window ({ex.GetType().Name}).");
        }
    }

    private void StartBrowser(SignInWindow window, SignInViewModel viewModel)
    {
        try
        {
            // Attach before the control joins the layout so its adapter events are not missed.
            var webView = new NativeWebView();
            browser.Attach(webView);
            window.SetBrowser(webView);
            viewModel.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error(ex, "The browser control could not be created");
            browser.Detach();
            viewModel.ReportStartFailure(ex.GetType().Name);
        }
    }
}
