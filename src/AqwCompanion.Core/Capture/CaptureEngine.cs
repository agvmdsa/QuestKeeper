using System.Text.Json;
using AqwCompanion.Core.Common;

namespace AqwCompanion.Core.Capture;

/// <summary>Counts of what the engine did, never any content.</summary>
public readonly record struct CaptureCounters(int SegmentsSeen, int SegmentsDropped, int MessagesDecoded, int CharactersCaptured);

/// <summary>How far the server data got, as counts only: never a command name or any content.</summary>
public readonly record struct CaptureStageCounters(int MessagesUnreadable, int ProfilePartsSeen, int InventoryPartsSeen, int OtherCommandsRead);

/// <summary>
/// Turns the TCP segments of the game traffic into one <see cref="CharacterSnapshot"/> per connection. Only server-to-client
/// segments (source port is a game port) enter the pipeline; everything else is dropped before any buffer. A connection that has
/// produced both the profile and the inventory stays as a LIVE connection, bound to its Character, until its FIN or RST (or a new
/// SYN-ACK on the same address pair). On a live connection encoded blobs, text and XML are never decoded (only counted) and a plain
/// JSON message is parsed only when a bounded scan of its first bytes finds one of the <see cref="LiveUpdates.Commands"/>; every
/// other plain message contributes its command name to the counts and nothing else. Nothing is stored except the Allow-listed parts.
/// </summary>
public sealed class CaptureEngine(TimeProvider clock)
{
    public const int MaxConnections = 16;

    private const int MaxFinishedConnections = 1024;

    private readonly object _gate = new();
    private readonly Dictionary<ConnectionKey, Connection> _connections = [];
    private readonly HashSet<ConnectionKey> _finished = [];
    private readonly Queue<ConnectionKey> _finishedOrder = new();
    private int _segmentsSeen;
    private int _segmentsDropped;
    private int _messagesDecoded;
    private int _charactersCaptured;
    private int _messagesUnreadable;
    private int _profilePartsSeen;
    private int _inventoryPartsSeen;
    private int _otherCommandsRead;
    private readonly int[] _failures = new int[Enum.GetValues<DecodeFailure>().Length];
    private readonly int[] _commands = new int[CaptureDecodeDiagnostics.KnownCommands.Count + 1];
    private readonly Dictionary<string, int> _otherNames = [];
    private readonly List<string> _otherNameOrder = [];
    private int _initUserDataAccepted;
    private int _initUserDataIgnoredNoGold;
    private int _initUserDataIgnoredNoName;
    private int _initUserDataIgnoredOther;
    private int _connectionsSeen;
    private int _connectionsWithSyn;
    private int _connectionsWithProfile;
    private int _connectionsWithInventory;
    private int _connectionsCompleted;
    private readonly List<UnreadableSample> _samples = [];
    private int _liveEventsApplied;
    private int _liveEventsIgnored;
    private int _liveEventsUnmatched;
    private int _liveEncodedSkipped;
    private long _lastGoldGain;
    private long _lastExpGain;
    private long _tick;

    /// <summary>Raised once per connection when its profile and inventory are both known.</summary>
    public event Action<CharacterSnapshot>? CharacterCaptured;

    /// <summary>Raised with the new snapshot of a Character every time a live event changed it.</summary>
    public event Action<CharacterSnapshot>? CharacterUpdated;

    /// <summary>What the engine has seen and done so far.</summary>
    public CaptureCounters Counters
    {
        get
        {
            lock (_gate)
            {
                return new CaptureCounters(_segmentsSeen, _segmentsDropped, _messagesDecoded, _charactersCaptured);
            }
        }
    }

    /// <summary>How far the server data got so far; counts only.</summary>
    public CaptureStageCounters Stages
    {
        get
        {
            lock (_gate)
            {
                return new CaptureStageCounters(_messagesUnreadable, _profilePartsSeen, _inventoryPartsSeen, _otherCommandsRead);
            }
        }
    }

    /// <summary>Why messages could not be read and which known commands were read; counts only, never any content; an unknown command name only when it passes <see cref="CaptureDecodeDiagnostics.IsListableName"/>.</summary>
    public CaptureDecodeDiagnostics Decoding
    {
        get
        {
            lock (_gate)
            {
                var failures = Enum.GetValues<DecodeFailure>().ToDictionary(f => f, f => _failures[(int)f]);
                var commands = new Dictionary<string, int>();
                for (var i = 0; i < CaptureDecodeDiagnostics.KnownCommands.Count; i++)
                {
                    commands[CaptureDecodeDiagnostics.KnownCommands[i]] = _commands[i];
                }

                commands[CaptureDecodeDiagnostics.OtherCommand] = _commands[^1];
                var ignored = _initUserDataIgnoredNoGold + _initUserDataIgnoredNoName + _initUserDataIgnoredOther;
                return new CaptureDecodeDiagnostics(
                    failures, commands, _initUserDataAccepted, ignored, _initUserDataIgnoredNoGold, _initUserDataIgnoredNoName,
                    [.. _otherNameOrder.Select(name => KeyValuePair.Create(name, _otherNames[name]))]);
            }
        }
    }

    /// <summary>How the connections went and the shapes of the first unreadable messages; counts and classes only, never any content.</summary>
    public CaptureConnectionDiagnostics Connections
    {
        get
        {
            lock (_gate)
            {
                return new CaptureConnectionDiagnostics(
                    _connectionsSeen, _connectionsWithSyn, _connectionsWithProfile, _connectionsWithInventory, _connectionsCompleted, [.. _samples]);
            }
        }
    }

    /// <summary>The live reading so far; numbers only.</summary>
    public CaptureLiveDiagnostics Live
    {
        get
        {
            lock (_gate)
            {
                return new CaptureLiveDiagnostics(
                    _connections.Values.Count(c => c.IsLive), _liveEventsApplied, _liveEventsIgnored, _liveEventsUnmatched,
                    _liveEncodedSkipped, _lastGoldGain, _lastExpGain);
            }
        }
    }

    /// <summary>The sink for <see cref="IPacketSource.RunAsync"/>. Never throws.</summary>
    public void Accept(TcpSegment segment)
    {
        List<Notification>? notifications;
        try
        {
            lock (_gate)
            {
                notifications = Route(segment);
            }
        }
        catch (Exception)
        {
            // Nothing from inside the pipeline may leave it, not even an exception.
            return;
        }

        if (notifications is null)
        {
            return;
        }

        foreach (var notification in notifications)
        {
            try
            {
                if (notification.Captured)
                {
                    CharacterCaptured?.Invoke(notification.Snapshot);
                }
                else
                {
                    CharacterUpdated?.Invoke(notification.Snapshot);
                }
            }
            catch (Exception)
            {
                // A failing subscriber must not stop the capture.
            }
        }
    }

    private List<Notification>? Route(TcpSegment segment)
    {
        _segmentsSeen++;
        _tick++;

        // Client-to-server segments (where the login travels) end here, before any buffer or decoder. A segment whose destination is
        // also a game port is ambiguous (a client whose own port falls in the game range): it is dropped as well.
        if (!GameTraffic.IsGamePort(segment.SourcePort) || GameTraffic.IsGamePort(segment.DestinationPort))
        {
            _segmentsDropped++;
            return null;
        }

        var key = ConnectionKey.From(segment);
        if (segment.IsSyn)
        {
            // The same address pair reused for a new connection: whatever a live connection knew is replaced.
            _finished.Remove(key);
            if (_connections.TryGetValue(key, out var old) && old.IsLive)
            {
                _connections.Remove(key);
            }
        }
        if (_finished.Contains(key))
        {
            _segmentsDropped++;
            return null;
        }

        if (!_connections.TryGetValue(key, out var connection))
        {
            if (_connections.Count >= MaxConnections && !(segment.IsSyn && EvictIdlestLiveConnection()))
            {
                _segmentsDropped++;
                return null;
            }
            connection = new Connection();
            _connections[key] = connection;
            _connectionsSeen++;
        }

        connection.LastTick = _tick;
        List<Notification>? notifications = null;

        if (segment.IsSyn && !connection.SawSyn)
        {
            connection.SawSyn = true;
            _connectionsWithSyn++;
        }

        foreach (var raw in connection.Reassembler.Feed(segment))
        {
            if (connection.IsLive)
            {
                ReadLive(connection, raw, ref notifications);
                continue;
            }

            using var message = MessageDecoder.TryDecode(raw, out var failure);
            if (message is null)
            {
                if (raw.Length > 0 && raw[0] != (byte)'%' && raw[0] != (byte)'<')
                {
                    _failures[(int)failure]++;
                    if (failure is DecodeFailure.NoCompressionHeader or DecodeFailure.InflateFailed or DecodeFailure.NotBase64
                        && _samples.Count < CaptureConnectionDiagnostics.MaxSamples)
                    {
                        var shape = MessageDecoder.Describe(raw);
                        _samples.Add(new UnreadableSample(raw.Length, shape.Reading, shape.DecodedLength, shape.FirstBytes, shape.EntropyBits));
                    }
                }

                if (raw.Length >= MessageDecoder.MinEncodedBytes && raw[0] != (byte)'%' && raw[0] != (byte)'<')
                {
                    _messagesUnreadable++;
                }

                continue;
            }

            _messagesDecoded++;
            var extracted = GameDataExtractor.Extract(message);
            CountCommand(message, extracted);
            switch (extracted)
            {
                case ProfilePart profile:
                    _profilePartsSeen++;
                    if (connection.Profile is null)
                    {
                        _connectionsWithProfile++;
                    }

                    connection.Profile = profile;
                    break;
                case InventoryPart inventory:
                    _inventoryPartsSeen++;
                    if (connection.Inventory is null)
                    {
                        _connectionsWithInventory++;
                    }

                    connection.Inventory = inventory;
                    break;
                default:
                    _otherCommandsRead++;
                    break;
            }

            if (connection.Profile is { } p && connection.Inventory is { } i)
            {
                var snapshot = new CharacterSnapshot(p.Profile, i.Factions, i.Items, i.HouseItems, clock.GetUtcNow());
                BecomeLive(key, connection, snapshot);
                _connectionsCompleted++;
                _charactersCaptured++;
                (notifications ??= []).Add(new Notification(true, snapshot));
            }
        }

        if (segment.IsFin || segment.IsRst)
        {
            // The server closed the connection (its data, if any, was read above): nothing more will come on it.
            Finish(key);
        }

        return notifications;
    }

    // The connection keeps its reassembler and is bound to its Character; an older live connection of the same Character goes.
    private void BecomeLive(ConnectionKey key, Connection connection, CharacterSnapshot snapshot)
    {
        foreach (var other in _connections.Where(c => c.Value.IsLive && c.Key != key
            && string.Equals(c.Value.Snapshot!.Profile.Name, snapshot.Profile.Name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _connections.Remove(other.Key);
        }

        connection.Snapshot = snapshot;
        connection.Profile = null;
        connection.Inventory = null;
    }

    private bool EvictIdlestLiveConnection()
    {
        var idlest = _connections.Where(c => c.Value.IsLive).OrderBy(c => c.Value.LastTick).Select(c => (ConnectionKey?)c.Key).FirstOrDefault();
        if (idlest is null)
        {
            return false;
        }

        _connections.Remove(idlest.Value);
        return true;
    }

    // One reassembled message of a live connection. Encoded blobs, text and XML are only counted. A plain JSON message is parsed
    // only when the bounded scan of its first bytes finds a live command; any other one contributes its name to the counts.
    private void ReadLive(Connection connection, byte[] raw, ref List<Notification>? notifications)
    {
        if (raw.Length == 0 || raw[0] != (byte)'{')
        {
            _liveEncodedSkipped++;
            return;
        }

        var name = LiveCommandScan.ReadName(raw);
        if (!LiveUpdates.IsLiveCommand(name))
        {
            CountCommandName(name);
            return;
        }

        CountCommandName(name);
        using var message = MessageDecoder.Decode(raw);
        if (message is null || !string.Equals(message.Command, name, StringComparison.Ordinal))
        {
            _liveEventsIgnored++;
            return;
        }

        var before = connection.Snapshot!;
        var result = LiveUpdates.Apply(before, message, connection.Cache);
        switch (result.Outcome)
        {
            case LiveUpdates.Applied:
                _liveEventsApplied++;
                break;
            case LiveUpdates.Unmatched:
                _liveEventsUnmatched++;
                break;
            default:
                _liveEventsIgnored++;
                break;
        }

        if (!result.Changed)
        {
            return;
        }

        if (name == "addGoldExp")
        {
            _lastGoldGain = result.Snapshot.Profile.Gold - before.Profile.Gold;
            _lastExpGain = result.Snapshot.Profile.Exp - before.Profile.Exp;
        }

        connection.Snapshot = result.Snapshot;
        (notifications ??= []).Add(new Notification(false, result.Snapshot));
    }

    // Only names from the fixed list are ever used as a command key; anything else is one number under "other", plus a
    // listing under its name when it has the restricted charset and there is room.
    private void CountCommand(GameMessage message, GameDataPart? extracted)
    {
        var index = CountCommandName(message.Command);
        if (index < 0 || message.Command != "initUserData")
        {
            return;
        }

        if (extracted is ProfilePart)
        {
            _initUserDataAccepted++;
        }
        else if (!HasProfileGold(message))
        {
            _initUserDataIgnoredNoGold++;
        }
        else if (!HasProfileName(message))
        {
            _initUserDataIgnoredNoName++;
        }
        else
        {
            _initUserDataIgnoredOther++;
        }
    }

    // Counts one message under its known command name, or under "other" (listing the name when it may be listed). Returns the index
    // of the known command, or -1.
    private int CountCommandName(string? name)
    {
        var index = -1;
        for (var i = 0; i < CaptureDecodeDiagnostics.KnownCommands.Count; i++)
        {
            if (string.Equals(CaptureDecodeDiagnostics.KnownCommands[i], name, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        _commands[index < 0 ? ^1 : index]++;
        if (index < 0)
        {
            CountOtherName(name);
        }

        return index;
    }

    private void CountOtherName(string? name)
    {
        if (!CaptureDecodeDiagnostics.IsListableName(name))
        {
            return;
        }

        if (_otherNames.TryGetValue(name!, out var count))
        {
            _otherNames[name!] = count + 1;
        }
        else if (_otherNames.Count < CaptureDecodeDiagnostics.MaxOtherNames)
        {
            _otherNames[name!] = 1;
            _otherNameOrder.Add(name!);
        }
    }

    private static JsonElement? ProfileData(GameMessage message) =>
        message.Document.RootElement.TryGetProperty("b", out var b) && b.ValueKind == JsonValueKind.Object
        && b.TryGetProperty("o", out var o) && o.ValueKind == JsonValueKind.Object
        && o.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : null;

    private static bool HasProfileGold(GameMessage message) => ProfileData(message)?.ReadLong("intGold") is not null;

    private static bool HasProfileName(GameMessage message) =>
        ProfileData(message)?.ReadCleanText("strUsername", GameDataExtractor.MaxCharacterNameLength) is not null;

    private void Finish(ConnectionKey key)
    {
        _connections.Remove(key);
        if (_finished.Add(key))
        {
            _finishedOrder.Enqueue(key);
        }
        while (_finishedOrder.Count > MaxFinishedConnections)
        {
            _finished.Remove(_finishedOrder.Dequeue());
        }
    }

    private readonly record struct Notification(bool Captured, CharacterSnapshot Snapshot);

    private sealed class Connection
    {
        public StreamReassembler Reassembler { get; } = new();

        /// <summary>The Character's newest snapshot once the login is complete; null before.</summary>
        public CharacterSnapshot? Snapshot { get; set; }

        public bool IsLive => Snapshot is not null;

        public LiveEventCache Cache { get; } = new();

        public long LastTick { get; set; }

        public bool SawSyn { get; set; }

        public ProfilePart? Profile { get; set; }

        public InventoryPart? Inventory { get; set; }
    }
}
