using AqwCompanion.Core.Forgetting;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>
/// Every Skua SkuaLink knows (up to <see cref="SkuaProtocol.MaxInstances"/> attached at once), each with its own status and
/// log (a <see cref="SkuaBridge"/>), and the command path to each plugin. UI-free; safe from any thread. All state changes
/// happen under one lock; events are raised and waiting commands completed outside it, and a throwing handler never stops
/// the others.
/// </summary>
/// <remarks>
/// Instances: a pipe is attached first and becomes an instance with its first line (normally the hello, whose
/// <see cref="SkuaHello.InstanceId"/> keys it; an older plugin without an id gets a generated one). A hello whose id is
/// known reattaches that instance. A closed pipe marks the instance disconnected; it stays (with its last status) until
/// SkuaLink restarts, except that beyond <see cref="MaxRetained"/> instances the oldest disconnected one is dropped. When a
/// connected instance reports a character that a disconnected instance played, that instance takes the connection over
/// (a restarted Skua comes back as the same chip, log and id).
/// Logs: an instance's log belongs to its character (<see cref="ISkuaLogStores.ForCharacter"/>); lines before the first
/// character join that character's log; another character in the same Skua switches to that character's log. The legacy
/// single log loads into the first instance that logs in, then is deleted.
/// </remarks>
public sealed class SkuaHub : ISkuaFeed, ISkuaCommandChannel, IForgettable
{
    /// <summary>How long a command waits for its result ("Skua did not answer").</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A start waits longer: script discovery and the first compile can take a while.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

    /// <summary>At most this many instances are kept, connected and disconnected together.</summary>
    public const int MaxRetained = 2 * SkuaProtocol.MaxInstances;

    /// <summary>The error of a command whose line would be over <see cref="SkuaProtocol.MaxCommandLineBytes"/>; it is never sent.</summary>
    public const string CommandTooLargeError = "command too large";

    private readonly object _lock = new();
    private readonly ISkuaLogStores _stores;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly List<SkuaInstance> _instances = [];
    private readonly HashSet<Attachment> _attached = [];
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private int _arrivals;
    private long _nextReqId;
    private bool _quiesced;
    private bool _legacyDone;
    private int _rejected;

    /// <param name="delay">The command timer; tests pass a fake so nothing sleeps. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public SkuaHub(ISkuaLogStores stores, Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _stores = stores;
        _now = now;
        _delay = delay ?? Task.Delay;
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Raised after a Forget. The pipe server closes every connection on it, so each plugin reconnects and sends its hello
    /// and its last status again (it otherwise re-sends a status only when it changes).
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

    public SkuaHubView Current
    {
        get
        {
            lock (_lock)
            {
                return new SkuaHubView([.. _instances.Select(i => i.View())]);
            }
        }
    }

    /// <summary>
    /// A new plugin pipe. Null when <see cref="SkuaProtocol.MaxInstances"/> pipes are attached already: the caller closes it
    /// quietly. Disposing the attachment says the pipe closed.
    /// </summary>
    public IDisposable? Attach(ISkuaConnection connection)
    {
        lock (_lock)
        {
            if (_attached.Count >= SkuaProtocol.MaxInstances)
            {
                return null;
            }

            var attachment = new Attachment(this, connection);
            _attached.Add(attachment);
            return attachment;
        }
    }

    /// <summary>A line of the plugin on that pipe could not be read.</summary>
    public void Reject(IDisposable attachment)
    {
        lock (_lock)
        {
            _rejected++;
        }
    }

    /// <summary>A message of the plugin on that pipe. Messages of a closed or forgotten pipe are ignored.</summary>
    public void Accept(IDisposable attachment, SkuaMessage message)
    {
        if (attachment is not Attachment pipe)
        {
            return;
        }

        Pending? answered = null;
        var changed = false;
        lock (_lock)
        {
            if (!_attached.Contains(pipe))
            {
                return;
            }

            if (message is ResultMessage result)
            {
                // One answer per reqId, and only from the instance the command went to; anything else is ignored.
                if (pipe.Instance is { } owner && _pending.TryGetValue(result.ReqId, out var pending) && pending.Instance == owner)
                {
                    _pending.Remove(result.ReqId);
                    answered = pending;
                }
            }
            else if (!_quiesced && message is not SkuaCommand)
            {
                var instance = pipe.Instance ?? Bind(pipe, (message as HelloMessage)?.Hello.InstanceId);
                instance.Bridge.Accept(message);
                if (message is StatusMessage { Status.Character: { } character } && !string.IsNullOrWhiteSpace(character))
                {
                    OnCharacter(instance, character);
                }

                changed = true;
            }
        }

        answered?.Answer.TrySetResult((ResultMessage)message);
        if (changed)
        {
            Raise(Changed);
        }
    }

    public async Task<ResultMessage?> SendCommandAsync(string instanceId, SkuaCommand command, CancellationToken ct)
    {
        Pending pending;
        ISkuaConnection connection;
        SkuaCommand outgoing;
        lock (_lock)
        {
            var instance = _instances.FirstOrDefault(i => i.Id == instanceId) ?? throw new KeyNotFoundException("unknown Skua");
            if (instance.Attachment is not Attachment pipe)
            {
                throw new InvalidOperationException("Skua is disconnected");
            }

            outgoing = command with { ReqId = "r" + ++_nextReqId };
            if (SkuaLine.ToCommandLine(outgoing) is null)
            {
                return new ResultMessage(outgoing.ReqId, false, CommandTooLargeError, null, null, null, false, false);
            }

            pending = new Pending(instance);
            _pending[outgoing.ReqId] = pending;
            connection = pipe.Connection;
        }

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            try
            {
                await connection.SendAsync(outgoing, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null;   // the pipe failed or its queue is full: as good as no answer
            }

            var timeout = _delay(outgoing is StartCommand ? StartTimeout : CommandTimeout, timer.Token);
            var done = await Task.WhenAny(pending.Answer.Task, timeout).ConfigureAwait(false);
            if (done == pending.Answer.Task)
            {
                return await pending.Answer.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            return null;
        }
        finally
        {
            await timer.CancelAsync().ConfigureAwait(false);
            lock (_lock)
            {
                if (_pending.TryGetValue(outgoing.ReqId, out var left) && left == pending)
                {
                    _pending.Remove(outgoing.ReqId);
                }
            }
        }
    }

    /// <summary>Saves every log that has unsaved lines.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            foreach (var instance in _instances)
            {
                instance.Bridge.Flush();
            }
        }
    }

    public void Quiesce()
    {
        lock (_lock)
        {
            _quiesced = true;
            foreach (var instance in _instances)
            {
                instance.Bridge.Quiesce();
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _quiesced = false;
            foreach (var instance in _instances)
            {
                instance.Bridge.Resume();
            }
        }
    }

    /// <summary>
    /// Deletes every Skua log file and drops every instance (their labels are character names): the pipes that are still open
    /// are no longer listened to, waiting commands complete with null, and the plugins reconnect after the server closes their
    /// pipes on <see cref="Forgotten"/>, starting over as <c>Skua 1</c>, <c>Skua 2</c>.
    /// </summary>
    public void Forget()
    {
        List<Pending> failed;
        lock (_lock)
        {
            foreach (var instance in _instances)
            {
                instance.Bridge.Forget();
            }

            _stores.DeleteAll();
            failed = [.. _pending.Values];
            _pending.Clear();
            foreach (var pipe in _attached)
            {
                pipe.Instance = null;
            }

            _attached.Clear();
            _instances.Clear();
            _arrivals = 0;
            _legacyDone = true;
            _quiesced = false;
        }

        Fail(failed);
        Raise(Changed);
        Raise(Forgotten);
    }

    private void Detach(Attachment pipe)
    {
        List<Pending> failed = [];
        var changed = false;
        lock (_lock)
        {
            if (!_attached.Remove(pipe))
            {
                return;
            }

            if (pipe.Instance is { } instance && instance.Attachment == pipe)
            {
                instance.Attachment = null;
                instance.Bridge.ClientDisconnected();
                failed = TakePending(instance);
                changed = true;
            }

            pipe.Instance = null;
        }

        Fail(failed);
        if (changed)
        {
            Raise(Changed);
        }
    }

    /// <summary>Under the lock: the instance for a pipe's first message.</summary>
    private SkuaInstance Bind(Attachment pipe, string? pluginId)
    {
        var instance = pluginId is null ? null : _instances.FirstOrDefault(i => i.PluginIds.Contains(pluginId));
        if (instance is null)
        {
            if (_instances.Count >= MaxRetained && _instances.FirstOrDefault(i => i.Disconnected) is { } oldest)
            {
                _instances.Remove(oldest);
            }

            instance = new SkuaInstance(pluginId ?? Guid.NewGuid().ToString("N"), ++_arrivals, new SkuaBridge(NoStore.Instance, _now));
            _instances.Add(instance);
        }
        else if (instance.Attachment is Attachment previous)
        {
            // The same plugin on a newer pipe (the old one has not been seen closing yet): the newer pipe wins, and the old
            // one is no longer listened to (its lines would otherwise make a new instance).
            previous.Instance = null;
            _attached.Remove(previous);
        }

        instance.Attachment = pipe;
        pipe.Instance = instance;
        instance.Bridge.ClientConnected();
        return instance;
    }

    /// <summary>Under the lock: a connected instance reported a character.</summary>
    private void OnCharacter(SkuaInstance instance, string character)
    {
        if (instance.Character is { } known && string.Equals(known, character, StringComparison.OrdinalIgnoreCase))
        {
            instance.Character = character;
            return;
        }

        var previous = _instances.FirstOrDefault(i =>
            i != instance && i.Disconnected && string.Equals(i.Character, character, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
        {
            TakeOver(instance, previous, character);
        }
        else
        {
            Rebind(instance, character);
        }
    }

    /// <summary>Under the lock: the disconnected instance of the same character takes the connection of the new one.</summary>
    private void TakeOver(SkuaInstance from, SkuaInstance to, string character)
    {
        if (from.Character is null)
        {
            to.Bridge.Import(from.Bridge.Log);
        }
        else
        {
            from.Bridge.Flush();
        }

        Replay(from.Bridge, to.Bridge);
        var pipe = (Attachment)from.Attachment!;
        to.PluginIds.UnionWith(from.PluginIds);
        to.Attachment = pipe;
        to.Character = character;
        pipe.Instance = to;
        foreach (var pending in _pending.Values.Where(p => p.Instance == from))
        {
            pending.Instance = to;
        }

        _instances.Remove(from);
    }

    /// <summary>Under the lock: the instance's log moves to the character's own log.</summary>
    private void Rebind(SkuaInstance instance, string character)
    {
        var old = instance.Bridge;
        old.Flush();
        var fresh = new SkuaBridge(_stores.ForCharacter(character), _now);
        if (instance.Character is null)
        {
            fresh.Import(old.Log);
        }

        if (!_legacyDone)
        {
            _legacyDone = true;
            var legacy = _stores.Legacy.Load();
            if (legacy.Count > 0)
            {
                fresh.Import(legacy);
            }

            _stores.Legacy.Delete();
        }

        Replay(old, fresh);
        instance.Bridge = fresh;
        instance.Character = character;
    }

    /// <summary>Under the lock: carries the connection, hello and status of a connected bridge over to another bridge.</summary>
    private static void Replay(SkuaBridge from, SkuaBridge to)
    {
        var view = from.Current;
        to.ClientConnected();
        if (view.Hello is { } hello)
        {
            to.Accept(new HelloMessage(hello));
        }

        if (view.Connection == SkuaConnection.VersionMismatch)
        {
            to.Accept(new UnsupportedVersionMessage(0));
        }

        if (view.Status is { } status)
        {
            to.Accept(new StatusMessage(status));
        }
    }

    private List<Pending> TakePending(SkuaInstance instance)
    {
        var taken = _pending.Where(p => p.Value.Instance == instance).ToList();
        foreach (var (reqId, _) in taken)
        {
            _pending.Remove(reqId);
        }

        return [.. taken.Select(p => p.Value)];
    }

    private static void Fail(List<Pending> pending)
    {
        foreach (var p in pending)
        {
            p.Answer.TrySetResult(null);
        }
    }

    private void Raise(EventHandler? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
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

    private sealed class Attachment(SkuaHub hub, ISkuaConnection connection) : IDisposable
    {
        public ISkuaConnection Connection { get; } = connection;

        /// <summary>Under the hub's lock: the instance this pipe feeds; null before its first message and after it was replaced.</summary>
        public SkuaInstance? Instance { get; set; }

        public void Dispose() => hub.Detach(this);
    }

    private sealed class Pending(SkuaInstance instance)
    {
        public SkuaInstance Instance { get; set; } = instance;

        public TaskCompletionSource<ResultMessage?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>The log of a Skua whose character is not known yet: kept in memory only, until the first character.</summary>
    private sealed class NoStore : ISkuaLogStore
    {
        public static readonly NoStore Instance = new();

        public IReadOnlyList<SkuaLogEntry> Load() => [];

        public void Save(IReadOnlyList<SkuaLogEntry> log)
        {
        }

        public void Delete()
        {
        }
    }
}
