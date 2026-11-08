using System.Globalization;
using AqwCompanion.App.Infrastructure.Formatting;
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
    public const string RefreshText = "Refresh numbers";
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
    public const string PrivacyText = "It starts by itself when the app opens and listens until you press Stop capture. It reads only the login data shown here, never your e-mail, IP address or password.";
    public const string EarlierSessionHintText = "A game session that started before this app was opened is running. Its data cannot be read until you log in again, for example by switching server.";
    public const string EmptyText = "The capture is already listening. Open the game launcher and log in; your data appears here when the login has been read.";

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
        RefreshCommand = new RelayCommand(RefreshNumbers);
        Refresh();
        session.Changed += (_, _) => _onUiThread(Refresh);
    }

    public IAsyncRelayCommand StartCommand { get; }

    public IRelayCommand StopCommand { get; }

    public IRelayCommand RestartAsAdministratorCommand { get; }

    /// <summary>Re-reads the capture numbers once; there is no timer.</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>Raised after the properties updated, when the selected Character's snapshot is another object than before.</summary>
    public event EventHandler? ActiveSnapshotChanged;

    public string StateText { get; private set; } = IdleText;

    public string CountersText { get; private set; } = string.Empty;

    /// <summary>Why messages were unreadable, as numbers only; empty when there is nothing to say.</summary>
    public string DecodingText { get; private set; } = string.Empty;

    public bool HasDecoding => DecodingText.Length > 0;

    /// <summary>How the connections went, as numbers only; empty before anything was seen.</summary>
    public string ConnectionsText { get; private set; } = string.Empty;

    public bool HasConnections => ConnectionsText.Length > 0;

    /// <summary>Warns that a game session started before the capture and cannot be read; empty when it does not apply.</summary>
    public string HintText { get; private set; } = string.Empty;

    public bool HasHint => HintText.Length > 0;

    /// <summary>The shapes of the first unreadable messages, as sizes and classes only; empty when there are none.</summary>
    public string SamplesText { get; private set; } = string.Empty;

    public bool HasSamples => SamplesText.Length > 0;

    /// <summary>How many messages of each known command were read, as numbers only; empty when there is nothing to say.</summary>
    public string CommandsText { get; private set; } = string.Empty;

    public bool HasCommands => CommandsText.Length > 0;

    /// <summary>The names of commands the capture does not know yet, with counts; empty when there are none.</summary>
    public string OtherNamesText { get; private set; } = string.Empty;

    public bool HasOtherNames => OtherNamesText.Length > 0;

    /// <summary>What the packet source says about its run (numbers and exception type names only), or empty.</summary>
    public string DetailsText { get; private set; } = string.Empty;

    public bool HasDetails => DetailsText.Length > 0;

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

    /// <summary>Says whether the selected Character was captured in this session or saved earlier; empty when there is none.</summary>
    public string CapturedText { get; private set; } = string.Empty;

    public bool HasCapturedText => CapturedText.Length > 0;

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
        StateText = StateTextFor(state, _session.Failure, _session.Counters.CharactersCaptured);
        ReadNumbers();

        _selectedCharacter = ChooseSelection();

        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CountersText));
        OnPropertyChanged(nameof(DecodingText));
        OnPropertyChanged(nameof(HasDecoding));
        OnPropertyChanged(nameof(CommandsText));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(OtherNamesText));
        OnPropertyChanged(nameof(HasOtherNames));
        OnPropertyChanged(nameof(OtherNamesText));
        OnPropertyChanged(nameof(HasOtherNames));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(HasHint));
        OnPropertyChanged(nameof(ConnectionsText));
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(SamplesText));
        OnPropertyChanged(nameof(HasSamples));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(HasDetails));
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

    private void RefreshNumbers()
    {
        ReadNumbers();
        OnPropertyChanged(nameof(CountersText));
        OnPropertyChanged(nameof(DecodingText));
        OnPropertyChanged(nameof(HasDecoding));
        OnPropertyChanged(nameof(CommandsText));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(HasHint));
        OnPropertyChanged(nameof(ConnectionsText));
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(SamplesText));
        OnPropertyChanged(nameof(HasSamples));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(HasDetails));
    }

    private void ReadNumbers()
    {
        var counters = _session.Counters;
        var stages = _session.Stages;
        CountersText = string.Create(
            CultureInfo.InvariantCulture,
            $"Segments seen {Format(counters.SegmentsSeen)} · dropped {Format(counters.SegmentsDropped)} · messages read {Format(counters.MessagesDecoded)} · characters captured {Format(counters.CharactersCaptured)} · unreadable {Format(stages.MessagesUnreadable)} · profile parts {Format(stages.ProfilePartsSeen)} · inventory parts {Format(stages.InventoryPartsSeen)}");
        var decoding = _session.Decoding;
        DecodingText = DecodingTextFor(decoding);
        CommandsText = CommandsTextFor(decoding);
        OtherNamesText = OtherNamesTextFor(decoding);
        var connections = _session.Connections;
        ConnectionsText = ConnectionsTextFor(connections);
        SamplesText = SamplesTextFor(connections);
        DetailsText = _session.Details;
        var startedEarlier = connections.ConnectionsSeen - connections.ConnectionsWithSyn > 0;
        var capturedNow = counters.CharactersCaptured > 0 || _session.Characters.Any(c => _session.IsCapturedThisSession(c.Profile.Name));
        HintText = startedEarlier && !capturedNow ? EarlierSessionHintText : string.Empty;
    }

    private static string ConnectionsTextFor(CaptureConnectionDiagnostics connections) =>
        connections.ConnectionsSeen == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Connections: {Format(connections.ConnectionsSeen)} seen, {Format(connections.ConnectionsWithSyn)} with the start of the stream, {Format(connections.ConnectionsWithProfile)} with a profile, {Format(connections.ConnectionsWithInventory)} with an inventory, {Format(connections.ConnectionsCompleted)} completed");

    private static string SamplesTextFor(CaptureConnectionDiagnostics connections)
    {
        if (connections.UnreadableSamples.Count == 0)
        {
            return string.Empty;
        }

        var parts = connections.UnreadableSamples.Select((s, index) => string.Create(
            CultureInfo.InvariantCulture,
            $"#{index + 1} {Format(s.EncodedLength)} B ({s.Reading}) -> {Format(s.DecodedLength)} B {s.FirstBytes}, entropy {s.EntropyBits:0.0}"));
        return "Unreadable messages: " + string.Join(" · ", parts);
    }

    private static string DecodingTextFor(CaptureDecodeDiagnostics decoding)
    {
        (string Label, DecodeFailure Reason)[] reasons =
        [
            ("not base64", DecodeFailure.NotBase64),
            ("no compression header", DecodeFailure.NoCompressionHeader),
            ("inflate failed", DecodeFailure.InflateFailed),
            ("too big", DecodeFailure.TooBigWhenInflated),
            ("not JSON", DecodeFailure.InflatedNotJson),
            ("invalid JSON", DecodeFailure.InvalidJson),
            ("no command", DecodeFailure.NoCommand),
            ("too large", DecodeFailure.TooLarge),
        ];
        var parts = reasons.Where(r => decoding.Failure(r.Reason) > 0).Select(r => $"{r.Label} {Format(decoding.Failure(r.Reason))}").ToList();
        return parts.Count == 0 ? string.Empty : "Unreadable: " + string.Join(" · ", parts);
    }

    private static string OtherNamesTextFor(CaptureDecodeDiagnostics decoding) =>
        decoding.OtherNames.Count == 0
            ? string.Empty
            : "Other commands seen: " + string.Join(" · ", decoding.OtherNames.Select(n => $"{n.Key} {Format(n.Value)}"));

    private static string CommandsTextFor(CaptureDecodeDiagnostics decoding)
    {
        var parts = new List<string>();
        foreach (var name in CaptureDecodeDiagnostics.KnownCommands.Append(CaptureDecodeDiagnostics.OtherCommand))
        {
            var count = decoding.Command(name);
            if (count == 0)
            {
                continue;
            }

            parts.Add(name == "initUserData"
                ? $"initUserData {Format(count)} (own profile {Format(decoding.InitUserDataAccepted)}, ignored {Format(decoding.InitUserDataIgnored)}: no gold {Format(decoding.InitUserDataIgnoredNoGold)}, no name {Format(decoding.InitUserDataIgnoredNoName)})"
                : $"{name} {Format(count)}");
        }

        return parts.Count == 0 ? string.Empty : "Commands read: " + string.Join(" · ", parts);
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
        CapturedText = snapshot is null ? string.Empty : CapturedTextFor(snapshot);
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
        OnPropertyChanged(nameof(CapturedText));
        OnPropertyChanged(nameof(HasCapturedText));
        OnPropertyChanged(nameof(ProfileLines));
        OnPropertyChanged(nameof(Factions));
        ApplyFilter();

        if (changed)
        {
            ActiveSnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string CapturedTextFor(CharacterSnapshot snapshot)
    {
        var time = LocalTimeText.Format(snapshot.CapturedAtUtc);
        return _session.IsCapturedThisSession(snapshot.Profile.Name)
            ? $"Captured {time} in this session."
            : $"Saved from an earlier session ({time}). Log in again to refresh it.";
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
        CaptureState.Listening => captured > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Listening. {Format(captured)} {(captured == 1 ? "character" : "characters")} captured so far. Log in another client to add more, or press Stop capture.")
            : ListeningText,
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
