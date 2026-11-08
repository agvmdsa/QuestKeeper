using AqwCompanion.App.Infrastructure.AutoStart;
using AqwCompanion.Core.Forgetting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Settings;

/// <summary>The Settings dialog: debug mode, the privacy text with Forget my data, the credits and the version.</summary>
public partial class SettingsViewModel : ObservableObject
{
    public const string TitleText = "Settings";
    public const string AutoStartTitleText = "Start with Windows";
    public const string AutoStartDescriptionText = "Opens the app in the notification area when you sign in to the PC, so the capture is already listening when you start the game.";
    public const string AutoStartFailedText = "Windows did not accept the change. Run the app as administrator and try again.";
    public const string DebugTitleText = "Debug mode";
    public const string DebugDescriptionText = "Shows the capture and packet details in their own window. It also opens with Ctrl+Shift+D and from the tray icon menu.";
    public const string PrivacyTitleText = "Privacy";
    public const string ForgetText = "Forget my data";
    public const string ForgetPromptText = "Remove everything the app saved on this computer? Your game data and the Hard Farms list are cleared until the next login.";
    public const string ForgetCompleteText = "Everything the app saved was removed from this computer.";
    public const string ForgetFailedText = "Could not remove the stored data. Close other copies of the app and try again.";
    public const string CreditsTitleText = "Credits";

    public const string PrivacyText =
        "AQW Companion reads, on this computer only, the data the game sends your own computer when you log in, and a few of your own events while you play: " +
        "character name and level, gold, experience, AC, class points, days played, membership days, faction reputation, and your items. " +
        "It never reads or keeps your e-mail address, IP address, password, user or character ids, or anything about other players. " +
        "It never sends anything to the game and never acts for you. Nothing leaves this computer; the only network access is to the AQW Wiki, " +
        "when you ask for the Hard Farms list. The data is saved locally so the app opens with it, and Forget my data removes it. " +
        "Reading the game's traffic is probably not allowed by the game's terms and can get an account banned: use a throwaway account.";

    public const string CreditsText =
        "Unofficial fan project, not made by, endorsed by or connected to Artix Entertainment. AdventureQuest Worlds belongs to its owners. " +
        "Reputation ranks and the Hard Farms list come from the AQW Wiki (CC BY-SA 3.0). Packet capture uses Npcap and SharpPcap.";

    private readonly IAppSettingsStore _store;
    private readonly ForgetCoordinator _forgetting;
    private readonly IAutoStart _autoStart;
    private bool _debugMode;
    private bool _startWithWindows;

    public SettingsViewModel(IAppSettingsStore store, ForgetCoordinator forgetting, IAutoStart? autoStart = null)
    {
        _store = store;
        _forgetting = forgetting;
        _autoStart = autoStart ?? new NoAutoStart();
        _debugMode = store.Load().DebugMode;
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
        get => _debugMode;
        set
        {
            if (_debugMode == value)
            {
                return;
            }

            _debugMode = value;
            _store.Save(new AppSettings(DebugMode: value));
            OnPropertyChanged();
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
