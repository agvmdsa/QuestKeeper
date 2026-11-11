using AqwCompanion.Core.Forgetting;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>
/// The Questkeeper side of the Skua link: connection state, the last status and a capped, persisted log.
/// Events and store calls happen outside the lock; a throwing handler never stops the others.
/// </summary>
public sealed class SkuaBridge : ISkuaFeed, IForgettable
{
    public const int Capacity = 500;
    public const int PhoneLogLimit = 100;
    public static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly object _io = new();
    private readonly ISkuaLogStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly LinkedList<SkuaLogEntry> _log;
    private SkuaHello? _hello;
    private SkuaStatus? _status;
    private SkuaConnection _connection = SkuaConnection.NotConnected;
    private bool _quiesced;
    private bool _dirty;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private int _rejected;
    private int _epoch;

    public SkuaBridge(ISkuaLogStore store, Func<DateTimeOffset> now)
    {
        _store = store;
        _now = now;
        _log = new LinkedList<SkuaLogEntry>(store.Load().OrderByDescending(e => e.At).Take(Capacity));
    }

    public event EventHandler? Changed;

    public string Name => "Skua log";

    public int Rejected
    {
        get
        {
            lock (_lock)
            {
                return _rejected;
            }
        }
    }

    public IReadOnlyList<SkuaLogEntry> Log
    {
        get
        {
            lock (_lock)
            {
                return [.. _log];
            }
        }
    }

    public SkuaView Current
    {
        get
        {
            lock (_lock)
            {
                return new SkuaView(_connection, _hello, _status, [.. _log.Take(PhoneLogLimit)]);
            }
        }
    }

    public void ClientConnected()
    {
        lock (_lock)
        {
            _connection = SkuaConnection.Connected;
        }

        RaiseChanged();
    }

    public void ClientDisconnected()
    {
        lock (_lock)
        {
            _connection = SkuaConnection.NotConnected;
        }

        Flush();
        RaiseChanged();
    }

    public void Reject()
    {
        lock (_lock)
        {
            _rejected++;
        }
    }

    public void Accept(SkuaMessage message)
    {
        IReadOnlyList<SkuaLogEntry>? snapshot = null;
        int epoch;
        lock (_lock)
        {
            if (_quiesced)
            {
                return;
            }

            epoch = _epoch;
            switch (message)
            {
                case HelloMessage hello:
                    _hello = hello.Hello;
                    if (_connection != SkuaConnection.VersionMismatch)
                    {
                        _connection = SkuaConnection.Connected;
                    }

                    break;
                case StatusMessage status:
                    _status = status.Status;
                    break;
                case LogMessage log:
                    _log.AddFirst(log.Entry);
                    while (_log.Count > Capacity)
                    {
                        _log.RemoveLast();
                    }

                    _dirty = true;
                    var now = _now();
                    if (now - _lastSave >= SaveEvery)
                    {
                        snapshot = TakeSnapshot(now);
                    }

                    break;
                case UnsupportedVersionMessage:
                    _connection = SkuaConnection.VersionMismatch;
                    break;
                default:
                    return;
            }
        }

        if (snapshot is not null)
        {
            Save(snapshot, epoch);
        }

        RaiseChanged();
    }

    /// <summary>Saves the log when there is unsaved data.</summary>
    public void Flush()
    {
        IReadOnlyList<SkuaLogEntry>? snapshot = null;
        int epoch;
        lock (_lock)
        {
            epoch = _epoch;
            if (_dirty)
            {
                snapshot = TakeSnapshot(_now());
            }
        }

        if (snapshot is not null)
        {
            Save(snapshot, epoch);
        }
    }

    public void Quiesce()
    {
        lock (_lock)
        {
            _quiesced = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _quiesced = false;
        }
    }

    public void Forget()
    {
        lock (_lock)
        {
            _log.Clear();
            _hello = null;
            _status = null;
            _dirty = false;
            _epoch++;
        }

        lock (_io)
        {
            _store.Delete();
        }

        RaiseChanged();
    }

    private List<SkuaLogEntry> TakeSnapshot(DateTimeOffset now)
    {
        _dirty = false;
        _lastSave = now;
        return [.. _log];
    }

    private void Save(IReadOnlyList<SkuaLogEntry> snapshot, int epoch)
    {
        lock (_io)
        {
            // A Forget since the snapshot must not be undone by a late save.
            lock (_lock)
            {
                if (_epoch != epoch)
                {
                    return;
                }
            }

            _store.Save(snapshot);
        }
    }

    private void RaiseChanged()
    {
        if (Changed is not { } changed)
        {
            return;
        }

        foreach (var handler in changed.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // One listener must never stop the others.
            }
        }
    }
}
