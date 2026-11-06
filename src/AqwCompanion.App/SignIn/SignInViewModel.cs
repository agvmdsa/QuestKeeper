using AqwCompanion.App.Infrastructure.Browser;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Site;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.SignIn;

public enum SignInState
{
    /// <summary>Waiting for the user to sign in, or for the account home page to load.</summary>
    SigningIn,

    /// <summary>On the account home page; the character name is being read.</summary>
    Reading,

    /// <summary>The name was read; "Connected" is shown briefly before the window closes.</summary>
    Success,

    /// <summary>A refresh landed on a page other than the account home page, so the user has to sign in again.</summary>
    SessionExpired,

    /// <summary>The read timed out or failed; the window stays open with the message.</summary>
    Error,

    /// <summary>The window was dismissed without a result.</summary>
    Cancelled,
}

/// <summary>
/// The flow behind the sign-in window. It reads the character name once the user, signed in by themselves,
/// is on the account home page. It only reacts to completed navigations; it never fills forms, clicks or
/// navigates on its own (the only navigation is the initial one and the one behind "Try again").
/// Pages outside account.aq.com are ignored entirely: they are neither scripted nor parsed.
/// </summary>
public sealed partial class SignInViewModel : ObservableObject, IDisposable
{
    public const string SigningInMessage =
        "Sign in on account.aq.com. This window closes once your character name has been read.";
    public const string UpdatingTitle = "Updating from account.aq.com...";
    public const string SigningOutTitle = "Signing out of account.aq.com...";
    public const string SigningOutMessage = "Ending your session on account.aq.com. This window closes when it is done.";
    public const string FullTitle = "Sign in to account.aq.com";
    public const string ReadingMessage = "Reading the character name...";
    public const string SessionExpiredMessage = "Your session expired. Please sign in again.";
    public const string ReadFailedMessage =
        "The character name could not be read from this page. To help diagnose it, open Diagnostics, tick "
        + "\"Include the raw page snapshot of the last failed capture\" and export. "
        + "Redaction is best-effort: review the zip before sharing it.";
    public const string ReadTimedOutMessage = "The page took too long to read. Try again.";
    public const string CouldNotReachMessage = "Could not reach account.aq.com. Check your connection and try again.";

    internal static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan DefaultNavigationTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan DefaultSuccessDelay = TimeSpan.FromMilliseconds(1200);

    private readonly IBrowserHost _host;
    private readonly IDelay _delay;
    private readonly TimeSpan _successDelay;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly NavigationWatchdog _watchdog;
    private readonly AccountHomeReader _reader;
    private readonly bool _signOut;
    private bool _unreachable;
    private bool _started;
    private bool _startFailed;
    private bool _reading;
    private bool _rerun;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsFull), nameof(IsCompact), nameof(ShowFullStatus), nameof(ShowFullSuccess),
        nameof(ShowCompactProgress), nameof(WindowTitle))]
    private SignInMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsSuccess), nameof(IsError), nameof(CanTryAgain), nameof(ShowBrowser), nameof(ShowFullStatus),
        nameof(ShowFullSuccess), nameof(ShowCompactProgress), nameof(CompactTitle))]
    private SignInState _state = SignInState.SigningIn;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuccessText), nameof(CompactTitle))]
    private string? _characterName;

    private AccountSummary? _summary;

    /// <param name="readTimeout">How long one page read may take; defaults to 10 seconds.</param>
    /// <param name="successDelay">How long "Connected" stays visible before the window closes; defaults to 1.2 seconds.</param>
    /// <param name="navigationTimeout">
    /// How long to wait for the first page to finish loading before giving up; defaults to 20 seconds.
    /// </param>
    /// <param name="logger">Where diagnostics go; defaults to the application logger. Never receives the character name or an address.</param>
    public SignInViewModel(
        IBrowserHost host,
        IngestionService<AccountIdentity> ingestion,
        IDelay delay,
        SignInMode mode,
        TimeSpan? readTimeout = null,
        TimeSpan? successDelay = null,
        TimeSpan? navigationTimeout = null,
        ILogger? logger = null)
    {
        _host = host;
        _delay = delay;
        _mode = mode;
        _signOut = mode == SignInMode.SignOut;
        _successDelay = successDelay ?? DefaultSuccessDelay;
        _log = logger ?? Log.Logger;
        _reader = new AccountHomeReader(host, ingestion, readTimeout ?? DefaultReadTimeout, _log);
        _watchdog = new NavigationWatchdog(
            navigationTimeout ?? DefaultNavigationTimeout, _log, () => IsFinished || _disposed, FailUnreachable);
        _lifetimeToken = _lifetime.Token;
        _message = InitialMessage(mode);
    }

    /// <summary>Raised once, when the window should close; carries the same value as <see cref="Result"/>.</summary>
    public event EventHandler<SignInResult>? CloseRequested;

    public SignInResult? Result { get; private set; }

    public bool IsFinished => Result is not null;

    public bool IsFull => Mode == SignInMode.Full;

    /// <summary>The progress layout: a refresh, or a sign-out (which only opens the logout page).</summary>
    public bool IsCompact => Mode is SignInMode.Compact or SignInMode.SignOut;

    /// <summary>This window only signs out: it opens the logout page and reads nothing.</summary>
    public bool IsSignOut => _signOut;

    public bool IsSuccess => State == SignInState.Success;

    public bool IsError => State == SignInState.Error;

    public bool CanTryAgain => IsError && !_startFailed;

    /// <summary>The browser is shown (or kept tiny in the compact layout) until the name has been read.</summary>
    public bool ShowBrowser => !IsSuccess;

    public bool ShowFullStatus => IsFull && !IsSuccess;

    public bool ShowFullSuccess => IsFull && IsSuccess;

    public bool ShowCompactProgress => IsCompact && !IsSuccess;

    public string WindowTitle => IsSignOut ? SigningOutTitle : IsCompact ? UpdatingTitle : FullTitle;

    public string CancelAutomationName => IsSignOut ? "Cancel signing out" : "Cancel updating from account.aq.com";

    public string SuccessText => $"Connected as {CharacterName}";

    public string CompactTitle => IsSuccess ? SuccessText : IsSignOut ? SigningOutTitle : UpdatingTitle;

    /// <summary>The running navigation watchdog; null while none is armed. Exposed so tests can await it.</summary>
    internal Task? NavigationWatchdog => _watchdog.Running;

    /// <summary>Begins listening to the browser and opens the account home page. Call once the browser exists.</summary>
    public void Start()
    {
        if (_started || IsFinished)
        {
            return;
        }

        _started = true;
        _host.NavigationCompleted += OnNavigationCompleted;
        if (_host is INavigationFailureSource failures)
        {
            failures.NavigationFailed += OnNavigationFailed;
        }

        State = SignInState.SigningIn;
        Message = InitialMessage(Mode);
        _watchdog.Start();
        _host.Navigate(StartUrl);
    }

    /// <summary>The browser control could not be created (for example the web view runtime is missing).</summary>
    public void ReportStartFailure(string reason)
    {
        _startFailed = true;
        Fail($"The browser could not be started: {reason}");
    }

    /// <summary>
    /// Closes the window. Dismissing while a failure is shown reports that failure; dismissing after the
    /// name has been read still reports the name; otherwise nothing changes.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        if (IsFinished)
        {
            return;
        }

        Finish(State switch
        {
            SignInState.Success => SignInResult.Connected(CharacterName!, _summary),
            SignInState.Error => SignInResult.Failed(Message),
            _ => SignInResult.Cancelled(),
        });
    }

    /// <summary>User-initiated retry after an error: opens the account home page again.</summary>
    [RelayCommand]
    public void TryAgain()
    {
        if (IsFinished || !CanTryAgain)
        {
            return;
        }

        if (IsSignOut)
        {
            Mode = SignInMode.SignOut;
        }

        State = SignInState.SigningIn;
        Message = ProgressMessage;
        _unreachable = false;
        _watchdog.Start();
        _host.Navigate(StartUrl);
    }

    /// <summary>Never throws: every failure ends up in <see cref="Message"/>.</summary>
    public async Task HandleNavigationAsync(string url)
    {
        if (IsFinished || _disposed || State == SignInState.Success)
        {
            return;
        }

        if (IsSignOut)
        {
            // About:blank or another site is not the logout request: ignore it so it neither ends the
            // watchdog nor reports a sign-out that has not happened yet.
            if (!IsAccountSite(url))
            {
                return;
            }

            NoteNavigation(url);
            // Whatever loaded, the logout page was requested and has finished: nothing is captured, parsed or stored.
            Finish(SignInResult.SignedOut());
            return;
        }

        NoteNavigation(url);
        if (!IsAccountSite(url))
        {
            return;
        }

        if (!AqwUrls.IsAccountHome(url))
        {
            OnLeftHome();
            return;
        }

        if (_reading)
        {
            // Remember it: the page may have changed since the running read started.
            _rerun = true;
            return;
        }

        _reading = true;
        try
        {
            await ReadUntilSettledAsync();
        }
        finally
        {
            _reading = false;
            _rerun = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // Finish the flow first (cancelling the in-flight read and the pause), then release everything.
            Cancel();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error("Closing the sign-in flow failed: {ExceptionType}", ex.GetType().Name);
        }

        _disposed = true;
        Unsubscribe();
        _watchdog.Stop();
        _lifetime.Dispose();
    }

    private string StartUrl => IsSignOut ? AqwUrls.Exit : AqwUrls.Home;

    private string ProgressMessage => IsSignOut ? SigningOutMessage : SigningInMessage;

    private static string InitialMessage(SignInMode mode) => mode switch
    {
        SignInMode.Compact => UpdatingTitle,
        SignInMode.SignOut => SigningOutMessage,
        _ => SigningInMessage,
    };

    private static bool IsAccountSite(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.EqualsIgnoreCase(AqwUrls.AccountHost);

    // async void: nothing may escape it, or an unhandled exception would take the application down.
    private async void OnNavigationCompleted(object? sender, NavigationCompletedEventArgs e)
    {
        try
        {
            await HandleNavigationAsync(e.Url);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error("Handling a completed navigation failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    private void OnNavigationFailed(object? sender, EventArgs e)
    {
        try
        {
            // Once a page has loaded the user may be browsing or signing in; a failed sub-navigation
            // must not interrupt that.
            if (IsFinished || _disposed || _watchdog.PageLoaded)
            {
                return;
            }

            _log.Warning("The first navigation failed");
            FailUnreachable();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error("Handling a failed navigation failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    /// <summary>
    /// The first completed navigation ends the watchdog: in the full layout any page counts (the user may now
    /// take as long as they like, on any domain); in a refresh only account.aq.com does, and landing anywhere
    /// else means there is no session, so the user is asked to sign in.
    /// </summary>
    private void NoteNavigation(string url)
    {
        if (!_watchdog.MarkPageLoaded())
        {
            return;
        }

        if (_unreachable)
        {
            // The page did arrive after all (a slow connection): drop the "could not reach" message.
            _unreachable = false;
            State = SignInState.SigningIn;
            Message = ProgressMessage;
        }

        if (Mode == SignInMode.Compact && !IsAccountSite(url))
        {
            ExpireSession();
        }
    }

    private void FailUnreachable()
    {
        Fail(CouldNotReachMessage);
        _unreachable = true;
    }

    private void Unsubscribe()
    {
        _host.NavigationCompleted -= OnNavigationCompleted;
        if (_host is INavigationFailureSource failures)
        {
            failures.NavigationFailed -= OnNavigationFailed;
        }
    }

    private void ExpireSession()
    {
        // A refresh that does not land on the home page means there is no session any more.
        Mode = SignInMode.Full;
        State = SignInState.SessionExpired;
        Message = SessionExpiredMessage;
    }

    private void OnLeftHome()
    {
        // The page is no longer the home page, so a pending re-read would read the wrong page.
        _rerun = false;
        if (State == SignInState.Error)
        {
            // Keep the failure visible until the user acts on it.
            return;
        }

        if (Mode == SignInMode.Compact)
        {
            ExpireSession();
        }
        else if (State != SignInState.SessionExpired)
        {
            State = SignInState.SigningIn;
            Message = SigningInMessage;
        }
    }

    private async Task ReadUntilSettledAsync()
    {
        AccountHomeRead outcome;
        do
        {
            _rerun = false;
            State = SignInState.Reading;
            if (IsFull)
            {
                Message = ReadingMessage;
            }

            outcome = await _reader.ReadAsync(_lifetimeToken, () => IsFinished || _disposed);
        }
        while (_rerun && !IsFinished);

        if (IsFinished || _disposed)
        {
            return;
        }

        if (outcome.Name is { } name)
        {
            await ShowSuccessAsync(name, outcome.Summary);
        }
        else
        {
            Fail(outcome.Failure switch
            {
                AccountHomeReadFailure.TimedOut => ReadTimedOutMessage,
                AccountHomeReadFailure.CaptureFailed => $"Could not capture the page: {outcome.CaptureError}",
                _ => ReadFailedMessage,
            });
        }
    }

    private async Task ShowSuccessAsync(string name, AccountSummary? summary)
    {
        if (IsFinished || _disposed)
        {
            return;
        }

        _rerun = false;
        CharacterName = name;
        _summary = summary;
        State = SignInState.Success;
        Message = SuccessText;
        try
        {
            await _delay.DelayAsync(_successDelay, _lifetimeToken);
        }
        catch (OperationCanceledException)
        {
            // Dismissed during the pause; the window is already closing.
            return;
        }

        Finish(SignInResult.Connected(name, summary));
    }

    private void Fail(string message)
    {
        if (IsFinished || _disposed)
        {
            return;
        }

        _watchdog.Stop();
        _unreachable = false;
        // The message can be long and the page is useful context, so the error always uses the full layout.
        Mode = SignInMode.Full;
        State = SignInState.Error;
        Message = message;
    }

    private void Finish(SignInResult result)
    {
        if (IsFinished)
        {
            return;
        }

        Result = result;
        if (result.Outcome == SignInOutcome.Cancelled)
        {
            State = SignInState.Cancelled;
        }

        _watchdog.Stop();
        Unsubscribe();
        if (!_disposed)
        {
            _lifetime.Cancel();
        }

        CloseRequested?.Invoke(this, result);
    }
}
