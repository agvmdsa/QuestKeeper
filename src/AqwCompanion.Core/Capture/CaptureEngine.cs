namespace AqwCompanion.Core.Capture;

/// <summary>Counts of what the engine did, never any content.</summary>
public readonly record struct CaptureCounters(int SegmentsSeen, int SegmentsDropped, int MessagesDecoded, int CharactersCaptured);

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
            using var message = MessageDecoder.Decode(raw);
            if (message is null)
            {
                continue;
            }

            _messagesDecoded++;
            switch (GameDataExtractor.Extract(message))
            {
                case ProfilePart profile:
                    connection.Profile = profile;
                    break;
                case InventoryPart inventory:
                    connection.Inventory = inventory;
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
