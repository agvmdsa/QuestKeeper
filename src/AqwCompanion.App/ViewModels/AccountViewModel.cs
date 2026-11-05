using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.ViewModels;

/// <summary>
/// Reads the character name once the user, signed in by themselves, is on the account home page.
/// It only reacts to completed navigations; it never fills forms, clicks or navigates on its own.
/// </summary>
public partial class AccountViewModel : ObservableObject
{
    public const string SignedOutMessage = "Not signed in. Click Sign in to open account.aq.com.";
    public const string WaitingMessage = "Waiting for sign-in...";
    public const string ReadingMessage = "Reading the character name...";
    public const string ReadMessage = "Character name read from the account home page.";
    public const string ReadFailedMessage =
        "The character name could not be read from this page. To help diagnose it, open Diagnostics, tick "
        + "\"Include the raw page snapshot of the last failed capture\" and export. "
        + "Redaction is best-effort: review the zip before sharing it.";
    public const string ReadTimedOutMessage = "The page took too long to read. Try again.";

    internal static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(10);

    private readonly IBrowserHost _host;
    private readonly IngestionService<AccountIdentity> _ingestion;
    private readonly TimeSpan _readTimeout;
    private bool _reading;
    private bool _rerun;

    [ObservableProperty]
    private bool _isBrowserOpen;

    [ObservableProperty]
    private string _status = SignedOutMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacter), nameof(CharacterText))]
    private string? _characterName;

    /// <summary>True when the shown name was read earlier and the browser has since left the home page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterText))]
    private bool _isCharacterStale;

    /// <param name="readTimeout">How long one page read may take; defaults to 10 seconds.</param>
    public AccountViewModel(
        IBrowserHost host,
        IngestionService<AccountIdentity> ingestion,
        TimeSpan? readTimeout = null)
    {
        _host = host;
        _ingestion = ingestion;
        _readTimeout = readTimeout ?? DefaultReadTimeout;
        _host.NavigationCompleted += async (_, e) => await HandleNavigationAsync(e.Url);
    }

    public IBrowserHost Browser => _host;

    public bool HasCharacter => CharacterName is not null;

    public string CharacterText => CharacterName is null
        ? string.Empty
        : $"{(IsCharacterStale ? "Last read" : "Character")}: {CharacterName}";

    [RelayCommand]
    private void SignIn() => OpenHome();

    [RelayCommand]
    private void OpenHome()
    {
        Status = WaitingMessage;
        _host.Navigate(AqwUrls.Home);
        IsBrowserOpen = true;
    }

    /// <summary>Never throws: every failure ends up in <see cref="Status"/>.</summary>
    public async Task HandleNavigationAsync(string url)
    {
        if (!AqwUrls.IsAccountHome(url))
        {
            // The page is no longer the home page, so a pending re-read would read the wrong page.
            _rerun = false;
            IsCharacterStale = CharacterName is not null;
            Status = WaitingMessage;
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
            do
            {
                _rerun = false;
                await ReadOnceAsync();
            }
            while (_rerun);
        }
        finally
        {
            _reading = false;
            _rerun = false;
        }
    }

    private async Task ReadOnceAsync()
    {
        Status = ReadingMessage;
        try
        {
            using var timeout = new CancellationTokenSource(_readTimeout);
            var snapshot = await _host.CaptureAsync(timeout.Token).WaitAsync(timeout.Token);
            var result = await _ingestion.IngestAsync(snapshot);
            if (result.Parse.Outcome == ParseOutcome.Ok && result.Parse.Data is { } identity)
            {
                CharacterName = identity.CharacterName;
                IsCharacterStale = false;
                Status = ReadMessage;
            }
            else
            {
                CharacterName = null;
                IsCharacterStale = false;
                Status = ReadFailedMessage;
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Reading the account home page timed out after {Timeout}", _readTimeout);
            IsCharacterStale = CharacterName is not null;
            Status = ReadTimedOutMessage;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error(ex, "Capturing the account home page failed");
            IsCharacterStale = CharacterName is not null;
            Status = $"Could not capture the page: {ex.Message}";
        }
    }
}
