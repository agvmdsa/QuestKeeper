using System.Globalization;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Equipment;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.Account;

/// <summary>
/// The Account tab: a card that says whether the character name is known and opens the sign-in window.
/// The browser lives only in that window; this view model just applies what the window reports and
/// hands the data to the card view models.
/// </summary>
public partial class AccountViewModel : ObservableObject
{
    public const string NotConnectedText = "Not connected";
    public const string SignedOutMessage = "Sign in to let the app read your character name from account.aq.com.";
    public const string ReadMessage = "Character name read from the account home page.";
    public const string CancelledMessage = "Cancelled. Nothing was changed.";
    public const string FailedFallbackMessage = "The character name could not be updated.";
    public const string StoredDataMessage = "Showing the data from your last session.";
    public const string StoredDataNoticeText = "Showing data saved on this computer. Refresh to update it.";
    public const string ForgottenMessage = "Stored data was removed from this computer.";
    public const string ForgetFailedMessage = "Could not remove the stored data. Close other copies of the app and try again.";

    private readonly ISignInDialog _dialog;
    private readonly ICharacterEquipmentLoader? _equipmentLoader;
    private readonly ICharacterBadgesLoader? _badgesLoader;
    private readonly IAccountDataPersistence? _persistence;
    private readonly TimeProvider _clock;
    private readonly IAccountSessionData? _session;
    private bool _dialogOpen;
    private CancellationTokenSource? _characterLoad;

    [ObservableProperty]
    private string _status = SignedOutMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(NeedsSignIn), nameof(Headline), nameof(ConnectionSummary), nameof(ShowSignIn))]
    [NotifyCanExecuteChangedFor(nameof(RequestForgetCommand))]
    private string? _characterName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoredDataNotice), nameof(HasStoredDataNotice), nameof(ShowSignIn))]
    private bool _isFromStoredData;

    [ObservableProperty]
    private bool _isForgetPending;

    /// <param name="summary">The account summary card.</param>
    /// <param name="equipment">The equipped card.</param>
    /// <param name="badges">The badges card.</param>
    /// <param name="sidebar">The details sidebar the cards open.</param>
    /// <param name="equipmentLoader">Reads the equipped items once the name is known; without it the panel stays empty.</param>
    /// <param name="badgesLoader">Reads the badges once the equipment has been read; without it the badges card stays empty.</param>
    /// <param name="persistence">Keeps the last read data on disk; without it nothing is stored or restored.</param>
    /// <param name="clock">Source of the read times; the system clock by default.</param>
    /// <param name="session">Follows restore and forget for the in-memory session data; without it nothing is seeded or cleared.</param>
    public AccountViewModel(
        ISignInDialog dialog,
        SummaryCardViewModel summary,
        EquipmentCardViewModel equipment,
        BadgesCardViewModel badges,
        SidebarViewModel sidebar,
        ICharacterEquipmentLoader? equipmentLoader = null,
        ICharacterBadgesLoader? badgesLoader = null,
        IAccountDataPersistence? persistence = null,
        TimeProvider? clock = null,
        IAccountSessionData? session = null)
    {
        _session = session;
        _dialog = dialog;
        Summary = summary;
        Equipment = equipment;
        Badges = badges;
        Sidebar = sidebar;
        _equipmentLoader = equipmentLoader;
        _badgesLoader = badgesLoader;
        _persistence = persistence;
        _clock = clock ?? TimeProvider.System;
        equipment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EquipmentCardViewModel.Data))
            {
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        };
    }

    public SummaryCardViewModel Summary { get; }

    public EquipmentCardViewModel Equipment { get; }

    public BadgesCardViewModel Badges { get; }

    public SidebarViewModel Sidebar { get; }

    public bool IsConnected => CharacterName is not null;

    /// <summary>The top bar text: "name · Lv level · faction"; level and faction appear once the character page is read.</summary>
    public string ConnectionSummary
    {
        get
        {
            if (CharacterName is null)
            {
                return string.Empty;
            }

            var parts = new List<string>(3) { CharacterName };
            if (Equipment.Data is { } equipment && string.Equals(equipment.Name, CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                if (equipment.Level is { } level)
                {
                    parts.Add(string.Create(CultureInfo.InvariantCulture, $"Lv {level}"));
                }

                if (!string.IsNullOrWhiteSpace(equipment.Faction))
                {
                    parts.Add(equipment.Faction);
                }
            }

            return string.Join(" \u00B7 ", parts);
        }
    }

    public bool NeedsSignIn => !IsConnected;

    /// <summary>Whether the Sign in button shows; stored data keeps it available because the saved session may have expired.</summary>
    public bool ShowSignIn => NeedsSignIn || IsFromStoredData;

    public string StoredDataNotice => IsFromStoredData ? StoredDataNoticeText : string.Empty;

    public bool HasStoredDataNotice => IsFromStoredData;

    public string Headline => CharacterName is null ? NotConnectedText : $"Connected as {CharacterName}";

    /// <summary>
    /// Shows the data kept from the last session. Called once at startup; reads the disk off the UI thread, never throws,
    /// and does nothing when a sign-in has already set a name.
    /// </summary>
    public async Task RestoreStoredDataAsync()
    {
        if (_persistence is null)
        {
            return;
        }

        try
        {
            var restored = await Task.Run(_persistence.Restore);
            if (restored is null || CharacterName is not null)
            {
                return;
            }

            CharacterName = restored.CharacterName;
            Summary.Data = restored.Summary;
            Equipment.Data = restored.Equipment;
            Badges.Data = restored.Badges;
            Summary.ReadAt = restored.IdentityReadAtUtc;
            Equipment.ReadAt = restored.Equipment is null ? null : restored.EquipmentReadAtUtc;
            Badges.ReadAt = restored.Badges is null ? null : restored.BadgesReadAtUtc;
            IsFromStoredData = true;
            Status = StoredDataMessage;
            _session?.Seed(restored);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Restoring the stored data failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    /// <summary>Asks for confirmation before the stored data is deleted; only possible while a character is shown.</summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void RequestForget()
    {
        if (IsConnected)
        {
            IsForgetPending = true;
        }
    }

    [RelayCommand]
    private void CancelForget() => IsForgetPending = false;

    /// <summary>Deletes the stored data and returns to the not-connected state. The browser session is not touched.</summary>
    [RelayCommand]
    private void ConfirmForget()
    {
        if (_persistence is not null && !_persistence.Forget())
        {
            IsForgetPending = false;
            Status = ForgetFailedMessage;
            return;
        }

        _characterLoad?.Cancel();
        _session?.Clear();
        CharacterName = null;
        Summary.Clear();
        Equipment.Clear();
        Badges.Clear();
        Sidebar.Reset();
        IsFromStoredData = false;
        IsForgetPending = false;
        Status = ForgottenMessage;
    }

    /// <summary>First sign-in: the browser is shown so the user can sign in themselves.</summary>
    [RelayCommand]
    private Task SignInAsync() => ShowAsync(SignInMode.Full);

    /// <summary>Reads the name again with the existing session; only a small progress window is shown.</summary>
    [RelayCommand]
    private Task RefreshAsync() => ShowAsync(SignInMode.Compact);

    private async Task ShowAsync(SignInMode mode)
    {
        if (_dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        SignInResult result;
        try
        {
            result = await _dialog.ShowAsync(mode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error(ex, "The sign-in window failed");
            result = SignInResult.Failed($"Could not open the sign-in window: {ex.Message}");
        }
        finally
        {
            _dialogOpen = false;
        }

        Apply(result);
        if (result is { Outcome: SignInOutcome.Connected, CharacterName: { } name })
        {
            await LoadCharacterAsync(name);
        }
    }

    /// <summary>
    /// Reads the public character page, then (when it carries a character id) the badges list. Runs after the dialog
    /// is closed, never throws, newest call wins.
    /// </summary>
    private async Task LoadCharacterAsync(string name)
    {
        if (_equipmentLoader is null)
        {
            return;
        }

        _characterLoad?.Cancel();
        using var load = new CancellationTokenSource();
        _characterLoad = load;

        if (!string.Equals(Equipment.Data?.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            Equipment.Data = null;
            Badges.Data = null;
            Equipment.ReadAt = null;
            Badges.ReadAt = null;
        }

        Equipment.Status = EquipmentCardViewModel.LoadingText;
        if (_badgesLoader is not null && Badges.Data is null)
        {
            Badges.Status = BadgesCardViewModel.LoadingText;
        }

        try
        {
            var loaded = await _equipmentLoader.LoadAsync(name, load.Token);
            if (load.IsCancellationRequested)
            {
                return;
            }

            switch (loaded.Status)
            {
                case EquipmentLoadStatus.Loaded:
                    Equipment.Data = loaded.Equipment;
                    Equipment.Status = string.Empty;
                    Equipment.ReadAt = null;
                    if (loaded is { Persisted: true, Equipment: { } toSave })
                    {
                        var now = _clock.GetUtcNow();
                        _persistence?.SaveEquipment(toSave, now);
                        Equipment.ReadAt = now;
                    }

                    break;
                case EquipmentLoadStatus.NotFound:
                    Equipment.Data = null;
                    Equipment.ReadAt = null;
                    Equipment.Status = EquipmentCardViewModel.NotFoundText;
                    break;
                default:
                    Equipment.Status = EquipmentCardViewModel.UnavailableText;
                    break;
            }

            await LoadBadgesAsync(loaded.Status == EquipmentLoadStatus.Loaded ? loaded.Equipment?.CharacterId : null, load);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load; that one owns the status texts.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Showing the character details failed: {ExceptionType}", ex.GetType().Name);
            if (!load.IsCancellationRequested)
            {
                Equipment.Status = EquipmentCardViewModel.UnavailableText;
                Badges.Status = _badgesLoader is not null ? BadgesCardViewModel.UnavailableText : string.Empty;
            }
        }
        finally
        {
            if (ReferenceEquals(_characterLoad, load))
            {
                _characterLoad = null;
            }
        }
    }

    /// <summary>Reads the badges list; skipped (with a status) when the character page gave no character id.</summary>
    private async Task LoadBadgesAsync(int? characterId, CancellationTokenSource load)
    {
        if (_badgesLoader is null)
        {
            return;
        }

        if (characterId is not { } id)
        {
            Badges.Status = BadgesCardViewModel.UnavailableText;
            return;
        }

        var loaded = await _badgesLoader.LoadAsync(id, load.Token);
        if (load.IsCancellationRequested)
        {
            return;
        }

        switch (loaded.Status)
        {
            case BadgesLoadStatus.Loaded:
                Badges.Data = loaded.Badges;
                Badges.Status = loaded.Badges is { Count: > 0 } ? string.Empty : BadgesCardViewModel.NotFoundText;
                Badges.ReadAt = null;
                if (loaded.Persisted)
                {
                    if (loaded.Badges is { Count: > 0 } toSave)
                    {
                        var now = _clock.GetUtcNow();
                        _persistence?.SaveBadges(toSave, now);
                        Badges.ReadAt = now;
                    }
                    else
                    {
                        _persistence?.RemoveBadges();
                    }
                }

                break;
            case BadgesLoadStatus.NotFound:
                Badges.Data = null;
                Badges.ReadAt = null;
                Badges.Status = BadgesCardViewModel.NotFoundText;
                break;
            default:
                Badges.Status = BadgesCardViewModel.UnavailableText;
                break;
        }
    }

    private void Apply(SignInResult result)
    {
        switch (result.Outcome)
        {
            case SignInOutcome.Connected when result.CharacterName is { } name:
                // A different character never keeps the previous one's stored equipment or badges.
                if (CharacterName is not null && !string.Equals(CharacterName, name, StringComparison.OrdinalIgnoreCase))
                {
                    // The stored records of the previous character are removed by SaveIdentity.
                    Equipment.Data = null;
                    Badges.Data = null;
                    Equipment.ReadAt = null;
                    Badges.ReadAt = null;
                }

                // A successful read replaces the summary, so a different character never shows the old one.
                CharacterName = name;
                Summary.Data = result.Summary;
                var now = _clock.GetUtcNow();
                _persistence?.SaveIdentity(name, result.Summary, now);
                Summary.ReadAt = now;
                IsFromStoredData = false;
                Status = ReadMessage;
                break;
            case SignInOutcome.Cancelled:
                Status = CancelledMessage;
                break;
            default:
                // A failed refresh keeps the last name and summary that were read; only the status changes.
                Status = result.Message ?? FailedFallbackMessage;
                break;
        }
    }
}
