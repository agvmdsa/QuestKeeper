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
/// produced both the profile and the inventory is forgotten and ignored from then on. Nothing is stored except the Allow-listed parts.
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
    private int _initUserDataAccepted;
    private int _initUserDataIgnoredNoGold;
    private int _initUserDataIgnoredNoName;
    private int _initUserDataIgnoredOther;

    /// <summary>Raised once per connection when its profile and inventory are both known.</summary>
    public event Action<CharacterSnapshot>? CharacterCaptured;

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

    /// <summary>Why messages could not be read and which known commands were read; counts only, never any content or unknown name.</summary>
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
                    failures, commands, _initUserDataAccepted, ignored, _initUserDataIgnoredNoGold, _initUserDataIgnoredNoName);
            }
        }
    }

    /// <summary>The sink for <see cref="IPacketSource.RunAsync"/>. Never throws.</summary>
    public void Accept(TcpSegment segment)
    {
        CharacterSnapshot? captured = null;
        try
        {
            lock (_gate)
            {
                captured = Route(segment);
            }
        }
        catch (Exception)
        {
            // Nothing from inside the pipeline may leave it, not even an exception.
            return;
        }

        if (captured is null)
        {
            return;
        }

        try
        {
            CharacterCaptured?.Invoke(captured);
        }
        catch (Exception)
        {
            // A failing subscriber must not stop the capture.
        }
    }

    private CharacterSnapshot? Route(TcpSegment segment)
    {
        _segmentsSeen++;

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
            // The same address pair reused for a new connection.
            _finished.Remove(key);
        }
        if (_finished.Contains(key))
        {
            _segmentsDropped++;
            return null;
        }

        if (!_connections.TryGetValue(key, out var connection))
        {
            if (_connections.Count >= MaxConnections)
            {
                _segmentsDropped++;
                return null;
            }
            connection = new Connection();
            _connections[key] = connection;
        }

        foreach (var raw in connection.Reassembler.Feed(segment))
        {
            using var message = MessageDecoder.TryDecode(raw, out var failure);
            if (message is null)
            {
                if (raw.Length > 0 && raw[0] != (byte)'%' && raw[0] != (byte)'<')
                {
                    _failures[(int)failure]++;
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
                    connection.Profile = profile;
                    break;
                case InventoryPart inventory:
                    _inventoryPartsSeen++;
                    connection.Inventory = inventory;
                    break;
                default:
                    _otherCommandsRead++;
                    break;
            }

            if (connection.Profile is { } p && connection.Inventory is { } i)
            {
                Finish(key);
                _charactersCaptured++;
                return new CharacterSnapshot(p.Profile, i.Factions, i.Items, i.HouseItems, clock.GetUtcNow());
            }
        }
        return null;
    }

    // Only names from the fixed list are ever used as a key; anything else is one number under "other".
    private void CountCommand(GameMessage message, GameDataPart? extracted)
    {
        var index = -1;
        for (var i = 0; i < CaptureDecodeDiagnostics.KnownCommands.Count; i++)
        {
            if (string.Equals(CaptureDecodeDiagnostics.KnownCommands[i], message.Command, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        _commands[index < 0 ? ^1 : index]++;
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

    private sealed class Connection
    {
        public StreamReassembler Reassembler { get; } = new();

        public ProfilePart? Profile { get; set; }

        public InventoryPart? Inventory { get; set; }
    }
}
