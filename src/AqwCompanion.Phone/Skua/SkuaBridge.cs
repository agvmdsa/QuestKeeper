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
    private long _snapshotSeq;
    private long _writtenSeq;

    public SkuaBridge(ISkuaLogStore store, Func<DateTimeOffset> now)
    {
        _store = store;
        _now = now;
        _log = new LinkedList<SkuaLogEntry>(store.Load().OrderByDescending(e => e.At).Take(Capacity));
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Raised after a Forget. The pipe server closes the current connection on it, so the plugin reconnects and sends its
    /// hello and its last status again (it otherwise re-sends a status only when it changes).
    /// </summary>
    public event EventHandler? Forgotten;

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
        long seq = 0;
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
                        snapshot = TakeSnapshot(now, out seq);
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
            Save(snapshot, epoch, seq);
        }

        RaiseChanged();
    }

    /// <summary>Saves the log when there is unsaved data.</summary>
    public void Flush()
    {
        IReadOnlyList<SkuaLogEntry>? snapshot = null;
        int epoch;
        long seq = 0;
        lock (_lock)
        {
            epoch = _epoch;
            if (_dirty)
            {
                snapshot = TakeSnapshot(_now(), out seq);
            }
        }

        if (snapshot is not null)
        {
            Save(snapshot, epoch, seq);
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

    /// <summary>
    /// Clears the log, the status and the stored file, and accepts messages again (the coordinator resumes only after a failed
    /// removal; every participant clears its own quiesced flag here). The hello is kept: it holds only the plugin and Skua
    /// version strings, no character data, and the plugin sends it only when it connects. The connection state is kept too.
    /// The I/O lock is held across the clear and the delete, so a save of an entry accepted after the clear always lands
    /// after the delete (a late save of a snapshot from before the clear is refused by the epoch).
    /// </summary>
    public void Forget()
    {
        lock (_io)
        {
            lock (_lock)
            {
                _log.Clear();
                _status = null;
                _dirty = false;
                _lastSave = DateTimeOffset.MinValue;
                _epoch++;
                _quiesced = false;
            }

            AfterClear?.Invoke();
            _store.Delete();
        }

        RaiseChanged();
        RaiseForgotten();
    }

    /// <summary>Test seam: runs inside Forget after the in-memory clear and before the store delete. Never set in production.</summary>
    internal Action? AfterClear { get; set; }

    /// <summary>
    /// Called under the state lock. A failed save is not retried until the next entry or flush: the store never throws, so there is nothing to retry on.
    /// </summary>
    private List<SkuaLogEntry> TakeSnapshot(DateTimeOffset now, out long seq)
    {
        _dirty = false;
        _lastSave = now;
        seq = ++_snapshotSeq;
        return [.. _log];
    }

    /// <summary>Test seam: runs after a snapshot is taken and before the write is ordered. Never set in production.</summary>
    internal Action? BeforeWrite { get; set; }

    private void Save(IReadOnlyList<SkuaLogEntry> snapshot, int epoch, long seq)
    {
        BeforeWrite?.Invoke();
        lock (_io)
        {
            // A Forget since the snapshot must not be undone by a late save (epoch), and an older snapshot
            // must not overwrite a newer one that already reached the store (sequence; it never resets, so
            // snapshots taken after a Forget always win over _writtenSeq values from before it).
            lock (_lock)
            {
                if (_epoch != epoch)
                {
                    return;
                }
            }

            if (seq <= _writtenSeq)
            {
                return;
            }

            _writtenSeq = seq;
            _store.Save(snapshot);
        }
    }

    private void RaiseChanged() => Raise(Changed);

    private void RaiseForgotten() => Raise(Forgotten);

    private void Raise(EventHandler? changed)
    {
        if (changed is null)
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
