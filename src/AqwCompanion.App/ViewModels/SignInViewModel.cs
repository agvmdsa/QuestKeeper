using AqwCompanion.App.Browser;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Site;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.ViewModels;

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
    private readonly IngestionService<AccountIdentity> _ingestion;
    private readonly IDelay _delay;
    private readonly TimeSpan _readTimeout;
    private readonly TimeSpan _successDelay;
    private readonly TimeSpan _navigationTimeout;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private CancellationTokenSource? _watchdog;
    private bool _pageLoaded;
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
        _ingestion = ingestion;
        _delay = delay;
        _mode = mode;
        _readTimeout = readTimeout ?? DefaultReadTimeout;
        _successDelay = successDelay ?? DefaultSuccessDelay;
        _navigationTimeout = navigationTimeout ?? DefaultNavigationTimeout;
        _log = logger ?? Log.Logger;
        _lifetimeToken = _lifetime.Token;
        _message = InitialMessage(mode);
    }

    /// <summary>Raised once, when the window should close; carries the same value as <see cref="Result"/>.</summary>
    public event EventHandler<SignInResult>? CloseRequested;

    public SignInResult? Result { get; private set; }

    public bool IsFinished => Result is not null;

    public bool IsFull => Mode == SignInMode.Full;

    public bool IsCompact => Mode == SignInMode.Compact;

    public bool IsSuccess => State == SignInState.Success;

    public bool IsError => State == SignInState.Error;

    public bool CanTryAgain => IsError && !_startFailed;

    /// <summary>The browser is shown (or kept tiny in the compact layout) until the name has been read.</summary>
    public bool ShowBrowser => !IsSuccess;

    public bool ShowFullStatus => IsFull && !IsSuccess;

    public bool ShowFullSuccess => IsFull && IsSuccess;

    public bool ShowCompactProgress => IsCompact && !IsSuccess;

    public string WindowTitle => IsCompact ? UpdatingTitle : FullTitle;

    public string SuccessText => $"Connected as {CharacterName}";

    public string CompactTitle => IsSuccess ? SuccessText : UpdatingTitle;

    /// <summary>The running navigation watchdog; null while none is armed. Exposed so tests can await it.</summary>
    internal Task? NavigationWatchdog { get; private set; }

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
        StartWatchdog();
        _host.Navigate(AqwUrls.Home);
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

        State = SignInState.SigningIn;
        Message = SigningInMessage;
        _unreachable = false;
        StartWatchdog();
        _host.Navigate(AqwUrls.Home);
    }

    /// <summary>Never throws: every failure ends up in <see cref="Message"/>.</summary>
    public async Task HandleNavigationAsync(string url)
    {
        if (IsFinished || _disposed || State == SignInState.Success)
        {
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
        StopWatchdog();
        _lifetime.Dispose();
    }

    private static string InitialMessage(SignInMode mode) => mode == SignInMode.Compact ? UpdatingTitle : SigningInMessage;

    private static bool IsAccountSite(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, AqwUrls.AccountHost, StringComparison.OrdinalIgnoreCase);

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
            if (IsFinished || _disposed || _pageLoaded)
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
        if (_pageLoaded)
        {
            return;
        }

        _pageLoaded = true;
        StopWatchdog();
        if (_unreachable)
        {
            // The page did arrive after all (a slow connection): drop the "could not reach" message.
            _unreachable = false;
            State = SignInState.SigningIn;
            Message = SigningInMessage;
        }

        if (Mode == SignInMode.Compact && !IsAccountSite(url))
        {
            ExpireSession();
        }
    }

    private void StartWatchdog()
    {
        StopWatchdog();
        if (_pageLoaded)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _watchdog = cts;
        NavigationWatchdog = WatchNavigationAsync(cts.Token);
    }

    private void StopWatchdog()
    {
        var cts = _watchdog;
        _watchdog = null;
        NavigationWatchdog = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
    }

    // Deliberately a real timer rather than IDelay: it must be independent of the "Connected" pause. The
    // continuation resumes on the UI thread because it is started from there.
    private async Task WatchNavigationAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_navigationTimeout, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (ct.IsCancellationRequested || IsFinished || _disposed)
            {
                return;
            }

            _log.Warning("No page finished loading within {Timeout}", _navigationTimeout);
            FailUnreachable();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error("The navigation watchdog failed: {ExceptionType}", ex.GetType().Name);
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
        ReadOutcome outcome;
        do
        {
            _rerun = false;
            State = SignInState.Reading;
            if (IsFull)
            {
                Message = ReadingMessage;
            }

            outcome = await ReadOnceAsync();
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
            Fail(outcome.Error ?? ReadFailedMessage);
        }
    }

    private async Task<ReadOutcome> ReadOnceAsync()
    {
        if (IsFinished || _disposed)
        {
            return new ReadOutcome(null, null, null);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
            timeout.CancelAfter(_readTimeout);
            var snapshot = await _host.CaptureAsync(timeout.Token).WaitAsync(timeout.Token);
            if (IsFinished || _disposed)
            {
                // Dismissed while capturing: nothing may be stored or buffered.
                return new ReadOutcome(null, null, null);
            }

            var result = await _ingestion.IngestAsync(snapshot, timeout.Token);
            if (IsFinished || _disposed)
            {
                return new ReadOutcome(null, null, null);
            }

            return result.Parse.Outcome == ParseOutcome.Ok && result.Parse.Data is { } identity
                ? new ReadOutcome(identity.CharacterName, null, identity.Summary)
                : new ReadOutcome(null, ReadFailedMessage, null);
        }
        catch (OperationCanceledException) when (IsFinished || _disposed || _lifetimeToken.IsCancellationRequested)
        {
            // Dismissed (or disposed) while reading: not a timeout, so nothing is reported.
            return new ReadOutcome(null, null, null);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Reading the account home page timed out after {Timeout}", _readTimeout);
            return new ReadOutcome(null, ReadTimedOutMessage, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(ex, "Capturing the account home page failed");
            return new ReadOutcome(null, $"Could not capture the page: {ex.Message}", null);
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

        StopWatchdog();
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

        StopWatchdog();
        Unsubscribe();
        if (!_disposed)
        {
            _lifetime.Cancel();
        }

        CloseRequested?.Invoke(this, result);
    }

    private readonly record struct ReadOutcome(string? Name, string? Error, AccountSummary? Summary);
}
