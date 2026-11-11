using AqwCompanion.App.Feed;
using AqwCompanion.App.Infrastructure.AutoStart;
using AqwCompanion.App.Phone;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.Settings;

/// <summary>The Settings dialog: debug mode, the privacy text with Forget my data, the credits and the version.</summary>
public partial class SettingsViewModel : ObservableObject
{
    public const string TitleText = "Settings";
    public const string AutoStartTitleText = "Start with Windows";
    public const string AutoStartDescriptionText = "Opens the app in the notification area when you sign in to the PC, so the capture is already listening when you start the game.";
    public const string AutoStartFailedText = "Windows did not accept the change. Run the app as administrator and try again.";
    public const string FeedTitleText = "Let other programs on this PC read my characters";
    public const string FeedDescriptionText = "Opens a read-only feed on this computer only (127.0.0.1) with your characters' numbers, factions and Hard Farms. A program needs the secret in the file below to read it. Nothing is sent to the game.";
    public const string FeedNewSecretText = "New secret";
    public const string FeedNewSecretWarningText = "Programs that use the old secret stop working until they read the new one.";
    public const string PhoneTitleText = "Watch my characters from my phone";
    public const string PhoneDescriptionText = "Shows your characters' numbers, factions and Hard Farms on a phone you pair, over your own Tailscale network. It only watches and never sends anything to the game.";
    public const string PairPhoneText = "Pair a phone...";
    public const string PhoneCopyLinkText = "Copy local pairing link";
    public const string DebugTitleText = "Debug mode";
    public const string DebugDescriptionText = "Shows the capture and packet details in their own window. It also opens with Ctrl+Shift+D and from the tray icon menu.";
    public const string PrivacyTitleText = "Privacy";
    public const string ForgetText = "Forget my data";
    public const string ForgetPromptText = "Remove everything the app saved on this computer? Your game data and the Hard Farms list are cleared until the next login.";
    public const string ForgetCompleteText = "Everything the app saved was removed from this computer.";
    public const string ForgetFailedText = "Could not remove the stored data. Close other copies of the app and try again.";
    public const string CreditsTitleText = "Credits";

    public const string PrivacyText =
        "Questkeeper reads, on this computer only, the data the game sends your own computer when you log in, and a few of your own events while you play: " +
        "character name and level, gold, experience, AC, class points, days played, membership days, faction reputation, and your items. " +
        "It never reads or keeps your e-mail address, IP address, password, user or character ids, or anything about other players. " +
        "It never sends anything to the game and never acts for you. Nothing leaves this computer; the only network access is to the AQW Wiki, " +
        "when you ask for the Hard Farms list. The data is saved locally so the app opens with it, and Forget my data removes it.";

    public const string CreditsText =
        "Unofficial fan project, not made by, endorsed by or connected to Artix Entertainment. AdventureQuest Worlds belongs to its owners. " +
        "Reputation ranks and the Hard Farms list come from the AQW Wiki (CC BY-SA 3.0). Packet capture uses Npcap and SharpPcap.";

    private readonly IAppSettingsStore _store;
    private readonly ForgetCoordinator _forgetting;
    private readonly IAutoStart _autoStart;
    private readonly FeedSecretStore? _feedSecret;
    private readonly PhoneRegistry? _phoneRegistry;
    private readonly Func<string, Task>? _copyToClipboard;
    private readonly Func<bool>? _isClipboardAvailable;
    private AppSettings _settings;
    private bool _startWithWindows;

    public SettingsViewModel(IAppSettingsStore store, ForgetCoordinator forgetting, IAutoStart? autoStart = null, FeedSecretStore? feedSecret = null,
        PhoneRegistry? phoneRegistry = null, Func<string, Task>? copyToClipboard = null, Func<bool>? isClipboardAvailable = null)
    {
        _phoneRegistry = phoneRegistry;
        _copyToClipboard = copyToClipboard;
        _isClipboardAvailable = isClipboardAvailable;
        _store = store;
        _forgetting = forgetting;
        _autoStart = autoStart ?? new NoAutoStart();
        _feedSecret = feedSecret;
        _settings = store.Load();
    }

    /// <summary>Raised after <see cref="LocalFeedEnabled"/> changed and was saved.</summary>
    public event EventHandler? LocalFeedChanged;

    /// <summary>Raised after the feed secret was replaced.</summary>
    public event EventHandler? FeedSecretRegenerated;

    /// <summary>What the local feed is doing, in words; set by the controller that runs it.</summary>
    [ObservableProperty]
    private string _feedStatusText = "Off";

    public void SetFeedStatus(string text) => FeedStatusText = text;

    /// <summary>Where the secret of the local feed is kept; empty when the app has no feed.</summary>
    public string FeedSecretPath => _feedSecret?.Path ?? string.Empty;

    public bool HasFeed => _feedSecret is not null;

    /// <summary>Whether the local read-only feed for other programs on this PC is on. Saved.</summary>
    public bool LocalFeedEnabled
    {
        get => _settings.LocalFeed;
        set
        {
            if (_settings.LocalFeed == value)
            {
                return;
            }

            _settings = _settings with { LocalFeed = value };
            _store.Save(_settings);
            OnPropertyChanged();
            LocalFeedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(HasFeed))]
    private void RegenerateFeedSecret()
    {
        _feedSecret?.Regenerate();
        FeedSecretRegenerated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after <see cref="PhoneAccessEnabled"/> changed and was saved.</summary>
    public event EventHandler? PhoneAccessChanged;

    /// <summary>What the phone access is doing, in words; set by the controller that runs it.</summary>
    [ObservableProperty]
    private string _phoneStatusText = "Off";

    public void SetPhoneStatus(string text) => PhoneStatusText = text;

    /// <summary>Raised when the dialog opens, so the phone controller can ask Tailscale again.</summary>
    public event EventHandler? PhoneRefreshRequested;

    /// <summary>What Tailscale offers right now, in a sentence; empty while the phone access is off. Set by the controller.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoneTailscaleText))]
    private string _phoneTailscaleText = string.Empty;

    /// <summary>Whether the phone page is published through Tailscale, so a pairing link can be made.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairPhoneCommand))]
    private bool _canPair;

    /// <summary>Raised when "Pair a phone..." is pressed; the dialog host opens the pairing window.</summary>
    public event EventHandler? PairRequested;

    [RelayCommand(CanExecute = nameof(CanPair))]
    private void PairPhone() => PairRequested?.Invoke(this, EventArgs.Empty);

    public bool HasPhoneTailscaleText => PhoneTailscaleText.Length > 0;

    public void SetPhoneTailscale(string text, bool canPair)
    {
        PhoneTailscaleText = text;
        CanPair = canPair;
    }

    public bool HasPhone => _phoneRegistry is not null;

    /// <summary>Whether the debug-only local pairing link is offered.</summary>
    public bool IsLocalPairingLinkVisible => HasPhone && DebugMode;

    /// <summary>Whether the phone page may be reached over the tailnet. Saved here, which is the source of truth for the switch.</summary>
    public bool PhoneAccessEnabled
    {
        get => _settings.PhoneAccess;
        set
        {
            if (_settings.PhoneAccess == value)
            {
                return;
            }

            _settings = _settings with { PhoneAccess = value };
            _store.Save(_settings);
            OnPropertyChanged();
            PhoneAccessChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Debug aid: puts a one-time pairing link for this PC on the clipboard. The link is never logged or shown.</summary>
    [RelayCommand(CanExecute = nameof(IsLocalPairingLinkVisible))]
    private async Task CopyLocalPairingLink()
    {
        if (!IsLocalPairingLinkVisible || _phoneRegistry is null || _copyToClipboard is null)
        {
            return;
        }

        // No token is made when there is nowhere to put it.
        if (_isClipboardAvailable is not null && !_isClipboardAvailable())
        {
            return;
        }

        try
        {
            await _copyToClipboard($"http://127.0.0.1:{PhoneHost.DefaultPort}/#t={_phoneRegistry.CreatePairingToken()}");
        }
        catch (Exception ex)
        {
            Log.Warning("Could not copy the pairing link: {ExceptionType}", ex.GetType().Name);
        }
    }

    /// <summary>Whether the switch for starting with Windows is offered at all.</summary>
    public bool CanAutoStart => _autoStart.IsSupported;

    /// <summary>Whether the app starts at sign-in. Setting it registers or removes the Windows task; a failure puts the switch back.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            if (_autoStart.TrySet(value))
            {
                _startWithWindows = value;
                AutoStartError = string.Empty;
            }
            else
            {
                AutoStartError = AutoStartFailedText;
            }

            // Also tells a switch that already moved to the new value to go back after a failure.
            OnPropertyChanged();
        }
    }

    /// <summary>Reads the state the system holds (the task can be deleted in Windows); call when the dialog opens.</summary>
    public void Refresh()
    {
        _startWithWindows = _autoStart.IsEnabled();
        AutoStartError = string.Empty;
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(CanAutoStart));
        PhoneRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoStartError))]
    private string _autoStartError = string.Empty;

    public bool HasAutoStartError => AutoStartError.Length > 0;

    private sealed class NoAutoStart : IAutoStart
    {
        public bool IsSupported => false;

        public bool IsEnabled() => false;

        public bool TrySet(bool enabled) => false;
    }

    /// <summary>Raised after <see cref="DebugMode"/> changed and was saved.</summary>
    public event EventHandler? DebugModeChanged;

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? CloseRequested;

    public string Version { get; } = DisplayVersion(AppInfo.Version);

    /// <summary>The version without the build hash that follows a plus sign.</summary>
    public static string DisplayVersion(string full) => full.Split('+')[0] is { Length: > 0 } version ? version : "0.0.0";

    public bool DebugMode
    {
        get => _settings.DebugMode;
        set
        {
            if (_settings.DebugMode == value)
            {
                return;
            }

            _settings = _settings with { DebugMode = value };
            _store.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalPairingLinkVisible));
            CopyLocalPairingLinkCommand.NotifyCanExecuteChanged();
            DebugModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [ObservableProperty]
    private bool _isForgetPending;

    /// <summary>The result of the last Forgetting; empty before one was asked for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasForgetStatus))]
    private string _forgetStatusText = string.Empty;

    public bool HasForgetStatus => ForgetStatusText.Length > 0;

    [RelayCommand]
    private void RequestForget() => IsForgetPending = true;

    [RelayCommand]
    private void CancelForget() => IsForgetPending = false;

    /// <summary>Forgets everything the app saved: stops work, removes the stored data, then clears memory.</summary>
    [RelayCommand]
    private void ConfirmForget()
    {
        IsForgetPending = false;
        var result = _forgetting.Forget();
        ForgetStatusText = !result.StoredDataRemoved
            ? ForgetFailedText
            : result.IsComplete
                ? ForgetCompleteText
                : ForgetCompleteText + " Some data could not be removed: " + string.Join(", ", result.NotForgotten) + ".";
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
