using System.Globalization;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Aqw;
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

    private readonly ISignInDialog _dialog;
    private readonly ICharacterEquipmentLoader? _equipmentLoader;
    private bool _dialogOpen;
    private CancellationTokenSource? _equipmentLoad;

    [ObservableProperty]
    private string _status = SignedOutMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(NeedsSignIn), nameof(Headline), nameof(ConnectionSummary))]
    private string? _characterName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEquipment), nameof(ConnectionSummary), nameof(EquippedPreviewSlots))]
    [NotifyCanExecuteChangedFor(nameof(OpenEquippedDetailsCommand))]
    private CharacterEquipment? _equipment;

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEquipmentStatus))]
    private string _equipmentStatus = string.Empty;

    /// <param name="equipmentLoader">Reads the equipped items once the name is known; without it the panel stays empty.</param>
    public AccountViewModel(ISignInDialog dialog, ICharacterEquipmentLoader? equipmentLoader = null)
    {
        _dialog = dialog;
        _equipmentLoader = equipmentLoader;
    }

    public bool HasEquipment => Equipment is not null;

    public bool HasEquipmentStatus => EquipmentStatus.Length > 0;

    public bool IsConnected => CharacterName is not null;

    /// <summary>The first few standard slots, shown on the dashboard card.</summary>
    public IEnumerable<EquipmentSlot> EquippedPreviewSlots => Equipment?.StandardSlots.Take(5) ?? [];

    public string SidebarTitle => "Equipped";

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

    public string Headline => CharacterName is null ? NotConnectedText : $"Connected as {CharacterName}";

    /// <summary>Opens the equipped details sidebar; only possible while there is equipment to show.</summary>
    [RelayCommand(CanExecute = nameof(HasEquipment))]
    private void OpenEquippedDetails() => IsSidebarOpen = true;

    [RelayCommand]
    private void CloseSidebar() => IsSidebarOpen = false;

    partial void OnEquipmentChanged(CharacterEquipment? value)
    {
        if (value is null)
        {
            IsSidebarOpen = false;
        }
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
            await LoadEquipmentAsync(name);
        }
    }

    /// <summary>Reads the public character page. Runs after the dialog is closed, never throws, newest call wins.</summary>
    private async Task LoadEquipmentAsync(string name)
    {
        if (_equipmentLoader is null)
        {
            return;
        }

        _equipmentLoad?.Cancel();
        using var load = new CancellationTokenSource();
        _equipmentLoad = load;

        if (!string.Equals(Equipment?.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            Equipment = null;
        }

        EquipmentStatus = EquipmentLoadingText;
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
                    break;
                case EquipmentLoadStatus.NotFound:
                    Equipment = null;
                    EquipmentStatus = EquipmentNotFoundText;
                    break;
                default:
                    EquipmentStatus = EquipmentUnavailableText;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load; that one owns the status text.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Showing the character equipment failed: {ExceptionType}", ex.GetType().Name);
            if (!load.IsCancellationRequested)
            {
                EquipmentStatus = EquipmentUnavailableText;
            }
        }
        finally
        {
            if (ReferenceEquals(_equipmentLoad, load))
            {
                _equipmentLoad = null;
            }
        }
    }

    private void Apply(SignInResult result)
    {
        switch (result.Outcome)
        {
            case SignInOutcome.Connected when result.CharacterName is { } name:
                CharacterName = name;
                Status = ReadMessage;
                break;
            case SignInOutcome.Cancelled:
                Status = CancelledMessage;
                break;
            default:
                // A failed refresh keeps the last name that was read; only the status changes.
                Status = result.Message ?? FailedFallbackMessage;
                break;
        }
    }
}
