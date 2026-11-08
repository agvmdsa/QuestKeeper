using System.Globalization;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Reputation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.GameData;

/// <summary>
/// The Game data tab: controls a <see cref="CaptureSession"/> and shows the selected Character's profile, faction standings
/// and items. The session raises its change event from capture threads, so the app passes <c>onUiThread</c> to run the
/// update on the UI thread (without it, inline). It never logs.
/// </summary>
public partial class GameDataViewModel : ObservableObject
{
    public const string TitleText = "Game data";
    public const string StartText = "Start capture";
    public const string StopText = "Stop capture";
    public const string RestartText = "Restart as administrator";
    public const string NpcapUrl = "https://npcap.com/";
    public const string IdleText = "Not capturing.";
    public const string NotSupportedText = "Capturing is only available on Windows.";
    public const string NpcapMissingText = "Npcap is not installed. Install it from https://npcap.com/ and press Start capture again.";
    public const string NeedsAdministratorText = "Capturing needs administrator rights.";
    public const string ListeningText = "Waiting for a login. Open the game launcher now.";
    public const string DoneText = "Capture finished.";
    public const string NoDeviceText = "No network device could be opened.";
    public const string DriverErrorText = "The capture driver reported an error.";
    public const string PrivacyText = "The app reads only the login data shown here. It never keeps your e-mail address, IP address or password.";
    public const string EmptyText = "Press Start capture, then open the game launcher and log in. Your data appears here when the login has been read.";

    private const int MaxRank = 10;

    private readonly CaptureSession _session;
    private readonly Action<Action> _onUiThread;

    private IReadOnlyList<CharacterSnapshot> _characters = [];
    private IReadOnlyList<GameItemRow> _allItems = [];
    private string? _selectedCharacter;
    private string _itemFilter = string.Empty;
    private CharacterSnapshot? _active;

    public GameDataViewModel(CaptureSession session, Action<Action>? onUiThread = null)
    {
        _session = session;
        _onUiThread = onUiThread ?? (action => action());
        StartCommand = new AsyncRelayCommand(session.StartAsync, () => CanStart);
        StopCommand = new RelayCommand(session.Stop, () => IsListening);
        RestartAsAdministratorCommand = new RelayCommand(() => session.RelaunchAsAdministrator(), () => NeedsAdministrator);
        Refresh();
        session.Changed += (_, _) => _onUiThread(Refresh);
    }

    public IAsyncRelayCommand StartCommand { get; }

    public IRelayCommand StopCommand { get; }

    public IRelayCommand RestartAsAdministratorCommand { get; }

    /// <summary>Raised after the properties updated, when the selected Character's snapshot is another object than before.</summary>
    public event EventHandler? ActiveSnapshotChanged;

    public string StateText { get; private set; } = IdleText;

    public string CountersText { get; private set; } = string.Empty;

    public bool IsListening { get; private set; }

    public bool CanStart => !IsListening;

    public bool NeedsAdministrator { get; private set; }

    public bool NpcapMissing { get; private set; }

    public IReadOnlyList<string> CharacterNames { get; private set; } = [];

    public bool HasCharacters => CharacterNames.Count > 0;

    public bool HasSeveralCharacters => CharacterNames.Count > 1;

    /// <summary>The selected Character's name, or null when there is none; a name that does not exist is ignored.</summary>
    public string? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (value is null || string.Equals(value, _selectedCharacter, StringComparison.Ordinal))
            {
                return;
            }

            var match = CharacterNames.FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _selectedCharacter = match;
                OnPropertyChanged();
                UpdateActive();
            }
        }
    }

    public string ProfileHeadline { get; private set; } = string.Empty;

    public IReadOnlyList<string> ProfileLines { get; private set; } = [];

    public IReadOnlyList<GameFactionRow> Factions { get; private set; } = [];

    public IReadOnlyList<GameItemRow> Items { get; private set; } = [];

    /// <summary>Item names containing this text (ignoring case) are listed.</summary>
    public string ItemFilter
    {
        get => _itemFilter;
        set
        {
            if (SetProperty(ref _itemFilter, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string ItemsSummary { get; private set; } = string.Empty;

    /// <summary>The selected Character's snapshot, or null.</summary>
    public CharacterSnapshot? ActiveSnapshot => _active;

    private void Refresh()
    {
        _characters = _session.Characters;
        CharacterNames = [.. _characters.Select(c => c.Profile.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

        var state = _session.State;
        IsListening = state == CaptureState.Listening;
        NeedsAdministrator = state == CaptureState.NeedsAdministrator;
        NpcapMissing = state == CaptureState.NpcapMissing;
        var counters = _session.Counters;
        StateText = StateTextFor(state, _session.Failure, counters.CharactersCaptured);
        CountersText = string.Create(
            CultureInfo.InvariantCulture,
            $"Segments seen {counters.SegmentsSeen.ToString("N0", CultureInfo.InvariantCulture)} · characters captured {counters.CharactersCaptured.ToString("N0", CultureInfo.InvariantCulture)}");

        _selectedCharacter = ChooseSelection();

        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CountersText));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(NeedsAdministrator));
        OnPropertyChanged(nameof(NpcapMissing));
        OnPropertyChanged(nameof(CharacterNames));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(HasSeveralCharacters));
        OnPropertyChanged(nameof(SelectedCharacter));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartAsAdministratorCommand.NotifyCanExecuteChanged();

        UpdateActive();
    }

    // Keeps the selection while that Character exists; otherwise the most recently captured one on a first selection, else the first by name.
    private string? ChooseSelection()
    {
        if (CharacterNames.Count == 0)
        {
            return null;
        }

        var kept = _selectedCharacter is null
            ? null
            : CharacterNames.FirstOrDefault(name => string.Equals(name, _selectedCharacter, StringComparison.OrdinalIgnoreCase));
        if (kept is not null)
        {
            return kept;
        }

        return _selectedCharacter is null
            ? _characters.OrderBy(c => c.CapturedAtUtc).Last().Profile.Name
            : CharacterNames[0];
    }

    private void UpdateActive()
    {
        var snapshot = _selectedCharacter is null
            ? null
            : _characters.FirstOrDefault(c => string.Equals(c.Profile.Name, _selectedCharacter, StringComparison.OrdinalIgnoreCase));
        var changed = !ReferenceEquals(snapshot, _active);
        _active = snapshot;

        ProfileHeadline = snapshot is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{snapshot.Profile.Name} · Lv {snapshot.Profile.Level}");
        ProfileLines = snapshot is null ? [] : ProfileLinesFor(snapshot.Profile);
        Factions = snapshot is null
            ? []
            : [.. snapshot.Factions.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(FactionRow)];
        _allItems = snapshot is null
            ? []
            : [.. snapshot.Items.Concat(snapshot.HouseItems)
                .OrderBy(i => i.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ItemRow)];
        if (snapshot is null && _itemFilter.Length > 0)
        {
            _itemFilter = string.Empty;
            OnPropertyChanged(nameof(ItemFilter));
        }

        OnPropertyChanged(nameof(ActiveSnapshot));
        OnPropertyChanged(nameof(ProfileHeadline));
        OnPropertyChanged(nameof(ProfileLines));
        OnPropertyChanged(nameof(Factions));
        ApplyFilter();

        if (changed)
        {
            ActiveSnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyFilter()
    {
        var needle = _itemFilter.Trim();
        Items = needle.Length == 0
            ? _allItems
            : [.. _allItems.Where(item => item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))];
        var noun = _allItems.Count == 1 ? "item" : "items";
        ItemsSummary = Items.Count == _allItems.Count
            ? string.Create(CultureInfo.InvariantCulture, $"{Format(_allItems.Count)} {noun}")
            : string.Create(CultureInfo.InvariantCulture, $"{Format(Items.Count)} of {Format(_allItems.Count)} {noun}");
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ItemsSummary));
    }

    private static string StateTextFor(CaptureState state, CaptureFailure failure, int captured) => state switch
    {
        CaptureState.NotSupported => NotSupportedText,
        CaptureState.NpcapMissing => NpcapMissingText,
        CaptureState.NeedsAdministrator => NeedsAdministratorText,
        CaptureState.Listening => ListeningText,
        CaptureState.Done => captured > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{DoneText} {Format(captured)} {(captured == 1 ? "character" : "characters")} captured.")
            : DoneText,
        CaptureState.Failed => failure == CaptureFailure.NoDevice ? NoDeviceText : DriverErrorText,
        _ => IdleText,
    };

    private static List<string> ProfileLinesFor(CharacterProfile p)
    {
        var lines = new List<string>
        {
            "Gold: " + Format(p.Gold),
            "AC: " + Format(p.Coins),
            "Experience: " + Format(p.Exp) + " / " + Format(p.ExpToLevel),
            "Class points: " + Format(p.ClassPoints),
            "Days played: " + Format(p.DaysPlayed),
            "Membership: " + MembershipText(p),
            "Bag slots: " + Format(p.BagSlots),
            "Bank slots: " + Format(p.BankSlots),
            "House slots: " + Format(p.HouseSlots),
            "Loadouts: " + Format(p.LoadoutSlots),
        };
        AddBoost(lines, "XP boost", p.BoostXp);
        AddBoost(lines, "Gold boost", p.BoostGold);
        AddBoost(lines, "Class points boost", p.BoostClassPoints);
        AddBoost(lines, "Reputation boost", p.BoostReputation);
        return lines;
    }

    private static void AddBoost(List<string> lines, string label, int value)
    {
        if (value != 0)
        {
            lines.Add(label + ": " + Format(value));
        }
    }

    private static string MembershipText(CharacterProfile p)
    {
        if (p.MembershipDays < 0)
        {
            var ago = -(long)p.MembershipDays;
            return "Expired " + Format(ago) + (ago == 1 ? " day ago" : " days ago");
        }

        return p.IsMember
            ? "Member, " + Format(p.MembershipDays) + (p.MembershipDays == 1 ? " day left" : " days left")
            : "Not a member";
    }

    private static GameFactionRow FactionRow(FactionStanding f)
    {
        var maxed = f.Rank >= MaxRank;
        return new GameFactionRow(
            f.Name,
            "Rank " + Format(f.Rank),
            Format(f.Rep),
            maxed ? "Max rank" : Format(f.SpillRep) + " / " + Format(f.RepToRank),
            maxed ? Format(0) : Format(Math.Max(0, f.RepToRank - f.SpillRep)));
    }

    private static GameItemRow ItemRow(OwnedItem item) =>
        new(item.Name, item.Type, Format(item.Quantity), Format(item.Level), FlagsText(item.Flags));

    private static string FlagsText(ItemFlags flags)
    {
        (ItemFlags Flag, string Text)[] order =
        [
            (ItemFlags.Coins, "AC"),
            (ItemFlags.Upgrade, "Member"),
            (ItemFlags.Equipped, "Equipped"),
            (ItemFlags.Worn, "Worn"),
            (ItemFlags.Quest, "Quest"),
            (ItemFlags.Bank, "Bank"),
            (ItemFlags.House, "House"),
            (ItemFlags.Temp, "Temp"),
        ];
        return string.Join(" · ", order.Where(entry => flags.HasFlag(entry.Flag)).Select(entry => entry.Text));
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
