using System.Globalization;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.Persistence;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.ViewModels;

/// <summary>
/// The Account tab: a card that says whether the character name is known and opens the sign-in window.
/// The browser lives only in that window; this view model just applies what the window reports.
/// </summary>
public partial class AccountViewModel : ObservableObject
{
    public const string NotConnectedText = "Not connected";
    public const string SignedOutMessage = "Sign in to let the app read your character name from account.aq.com.";
    public const string ReadMessage = "Character name read from the account home page.";
    public const string CancelledMessage = "Cancelled. Nothing was changed.";
    public const string FailedFallbackMessage = "The character name could not be updated.";
    public const string EquipmentLoadingText = "Loading equipment...";
    public const string EquipmentUnavailableText = "Could not load the character page. Try Refresh again.";
    public const string EquipmentNotFoundText = "No equipment found on the character page.";
    public const string BadgesLoadingText = "Loading badges...";
    public const string BadgesUnavailableText = "Could not load the badges. Try Refresh again.";
    public const string BadgesNotFoundText = "No badges found for this character.";
    public const string SummaryUnavailableText = "Not available. Try Refresh.";
    public const string MissingValueText = "\u2014";
    public const string StoredDataMessage = "Showing the data from your last session.";
    public const string StoredDataNoticeText = "Showing data saved on this computer. Refresh to update it.";
    public const string ForgottenMessage = "Stored data was removed from this computer.";
    public const string ForgetFailedMessage = "Could not remove the stored data. Close other copies of the app and try again.";

    /// <summary>Which card the details sidebar is showing.</summary>
    public enum SidebarCard
    {
        Equipped,
        Badges,
    }

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
    [NotifyPropertyChangedFor(
        nameof(HasSummary), nameof(AdventureCoinsText), nameof(AccountTypeText), nameof(MembershipText), nameof(GearsOfDoomText))]
    private AccountSummary? _summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEquipment), nameof(ConnectionSummary), nameof(EquippedPreviewSlots))]
    [NotifyCanExecuteChangedFor(nameof(OpenEquippedDetailsCommand))]
    private CharacterEquipment? _equipment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadges), nameof(BadgeCountText), nameof(BadgesPreview), nameof(BadgeGroups))]
    [NotifyCanExecuteChangedFor(nameof(OpenBadgesDetailsCommand))]
    private CharacterBadges? _badges;

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarTitle), nameof(SidebarAutomationName), nameof(IsEquippedSidebar), nameof(IsBadgesSidebar))]
    private SidebarCard _sidebarContent = SidebarCard.Equipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEquipmentStatus))]
    private string _equipmentStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadgesStatus))]
    private string _badgesStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryUpdatedText), nameof(HasSummaryUpdatedText))]
    private DateTimeOffset? _identityReadAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EquipmentUpdatedText), nameof(HasEquipmentUpdatedText))]
    private DateTimeOffset? _equipmentReadAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgesUpdatedText), nameof(HasBadgesUpdatedText))]
    private DateTimeOffset? _badgesReadAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoredDataNotice), nameof(HasStoredDataNotice), nameof(ShowSignIn))]
    private bool _isFromStoredData;

    [ObservableProperty]
    private bool _isForgetPending;

    /// <param name="equipmentLoader">Reads the equipped items once the name is known; without it the panel stays empty.</param>
    /// <param name="badgesLoader">Reads the badges once the equipment has been read; without it the badges card stays empty.</param>
    /// <param name="persistence">Keeps the last read data on disk; without it nothing is stored or restored.</param>
    /// <param name="clock">Source of the read times; the system clock by default.</param>
    /// <param name="session">Follows restore and forget for the in-memory session data; without it nothing is seeded or cleared.</param>
    public AccountViewModel(
        ISignInDialog dialog,
        ICharacterEquipmentLoader? equipmentLoader = null,
        ICharacterBadgesLoader? badgesLoader = null,
        IAccountDataPersistence? persistence = null,
        TimeProvider? clock = null,
        IAccountSessionData? session = null)
    {
        _session = session;
        _dialog = dialog;
        _equipmentLoader = equipmentLoader;
        _badgesLoader = badgesLoader;
        _persistence = persistence;
        _clock = clock ?? TimeProvider.System;
    }

    public bool HasEquipment => Equipment is not null;

    public bool HasEquipmentStatus => EquipmentStatus.Length > 0;

    /// <summary>True when there is at least one badge to show; an empty list is reported through the status text.</summary>
    public bool HasBadges => Badges is { Count: > 0 };

    public bool HasBadgesStatus => BadgesStatus.Length > 0;

    public bool HasSummary => Summary is not null;

    /// <summary>AdventureCoins with thousands separators, or a dash when the page did not carry it.</summary>
    public string AdventureCoinsText =>
        Summary?.AdventureCoins is { } coins ? coins.ToString("N0", CultureInfo.InvariantCulture) : MissingValueText;

    public string AccountTypeText => Summary?.AccountType ?? MissingValueText;

    /// <summary>The page's own membership line, date included.</summary>
    public string MembershipText => Summary?.MembershipStatus ?? MissingValueText;

    public string GearsOfDoomText =>
        Summary?.GearsOfDoom is { } gears ? gears.ToString(CultureInfo.InvariantCulture) : MissingValueText;

    public bool IsConnected => CharacterName is not null;

    /// <summary>The first few standard slots, shown on the dashboard card.</summary>
    public IEnumerable<EquipmentSlot> EquippedPreviewSlots => Equipment?.StandardSlots.Take(5) ?? [];

    /// <summary>The first few badges, shown on the dashboard card.</summary>
    public IEnumerable<Badge> BadgesPreview => Badges?.Preview ?? [];

    /// <summary>All badges grouped by category, shown in the sidebar.</summary>
    public IReadOnlyList<BadgeGroup> BadgeGroups => Badges?.GroupedByCategory ?? [];

    public string BadgeCountText => Badges is { Count: 1 } ? "1 badge" : $"{Badges?.Count ?? 0} badges";

    public bool IsEquippedSidebar => SidebarContent == SidebarCard.Equipped;

    public bool IsBadgesSidebar => SidebarContent == SidebarCard.Badges;

    public string SidebarTitle => IsBadgesSidebar ? "Badges" : "Equipped";

    public string SidebarAutomationName => $"{SidebarTitle} details";

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
            if (Equipment is { } equipment && string.Equals(equipment.Name, CharacterName, StringComparison.OrdinalIgnoreCase))
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

    public string SummaryUpdatedText => UpdatedText(IdentityReadAt);

    public bool HasSummaryUpdatedText => IdentityReadAt is not null;

    public string EquipmentUpdatedText => UpdatedText(EquipmentReadAt);

    public bool HasEquipmentUpdatedText => EquipmentReadAt is not null;

    public string BadgesUpdatedText => UpdatedText(BadgesReadAt);

    public bool HasBadgesUpdatedText => BadgesReadAt is not null;

    private static string UpdatedText(DateTimeOffset? at) =>
        at is { } value ? "Last updated " + value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty;

    public string Headline => CharacterName is null ? NotConnectedText : $"Connected as {CharacterName}";

    /// <summary>Opens the equipped details sidebar; only possible while there is equipment to show.</summary>
    [RelayCommand(CanExecute = nameof(HasEquipment))]
    private void OpenEquippedDetails()
    {
        SidebarContent = SidebarCard.Equipped;
        IsSidebarOpen = true;
    }

    /// <summary>Opens the badges sidebar; only possible while there are badges to show.</summary>
    [RelayCommand(CanExecute = nameof(HasBadges))]
    private void OpenBadgesDetails()
    {
        SidebarContent = SidebarCard.Badges;
        IsSidebarOpen = true;
    }

    [RelayCommand]
    private void CloseSidebar() => IsSidebarOpen = false;

    partial void OnEquipmentChanged(CharacterEquipment? value)
    {
        if (value is null && IsEquippedSidebar)
        {
            IsSidebarOpen = false;
        }
    }

    partial void OnBadgesChanged(CharacterBadges? value)
    {
        if (value is not { Count: > 0 } && IsBadgesSidebar)
        {
            IsSidebarOpen = false;
        }
    }

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
            Summary = restored.Summary;
            Equipment = restored.Equipment;
            Badges = restored.Badges;
            IdentityReadAt = restored.IdentityReadAtUtc;
            EquipmentReadAt = restored.Equipment is null ? null : restored.EquipmentReadAtUtc;
            BadgesReadAt = restored.Badges is null ? null : restored.BadgesReadAtUtc;
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
        SidebarContent = SidebarCard.Equipped;
        CharacterName = null;
        Summary = null;
        Equipment = null;
        Badges = null;
        IdentityReadAt = null;
        EquipmentReadAt = null;
        BadgesReadAt = null;
        EquipmentStatus = string.Empty;
        BadgesStatus = string.Empty;
        IsSidebarOpen = false;
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

        if (!string.Equals(Equipment?.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            Equipment = null;
            Badges = null;
            EquipmentReadAt = null;
            BadgesReadAt = null;
        }

        EquipmentStatus = EquipmentLoadingText;
        if (_badgesLoader is not null && Badges is null)
        {
            BadgesStatus = BadgesLoadingText;
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
                    Equipment = loaded.Equipment;
                    EquipmentStatus = string.Empty;
                    EquipmentReadAt = null;
                    if (loaded is { Persisted: true, Equipment: { } toSave })
                    {
                        var now = _clock.GetUtcNow();
                        _persistence?.SaveEquipment(toSave, now);
                        EquipmentReadAt = now;
                    }

                    break;
                case EquipmentLoadStatus.NotFound:
                    Equipment = null;
                    EquipmentReadAt = null;
                    EquipmentStatus = EquipmentNotFoundText;
                    break;
                default:
                    EquipmentStatus = EquipmentUnavailableText;
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
                EquipmentStatus = EquipmentUnavailableText;
                BadgesStatus = _badgesLoader is not null ? BadgesUnavailableText : string.Empty;
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
            BadgesStatus = BadgesUnavailableText;
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
                Badges = loaded.Badges;
                BadgesStatus = loaded.Badges is { Count: > 0 } ? string.Empty : BadgesNotFoundText;
                BadgesReadAt = null;
                if (loaded.Persisted)
                {
                    if (loaded.Badges is { Count: > 0 } toSave)
                    {
                        var now = _clock.GetUtcNow();
                        _persistence?.SaveBadges(toSave, now);
                        BadgesReadAt = now;
                    }
                    else
                    {
                        _persistence?.RemoveBadges();
                    }
                }

                break;
            case BadgesLoadStatus.NotFound:
                Badges = null;
                BadgesReadAt = null;
                BadgesStatus = BadgesNotFoundText;
                break;
            default:
                BadgesStatus = BadgesUnavailableText;
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
                    Equipment = null;
                    Badges = null;
                    EquipmentReadAt = null;
                    BadgesReadAt = null;
                }

                // A successful read replaces the summary, so a different character never shows the old one.
                CharacterName = name;
                Summary = result.Summary;
                var now = _clock.GetUtcNow();
                _persistence?.SaveIdentity(name, result.Summary, now);
                IdentityReadAt = now;
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
