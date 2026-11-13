namespace AqwCompanion.Skua;

/// <summary>
/// What the command handler needs from Skua. The plugin implements it over Skua's <c>IScriptManager</c>; the tests fake it.
/// Any member may throw: the handler turns every failure into an error answer.
/// </summary>
public interface ISkuaScriptHost
{
    /// <summary>Skua's <c>ScriptRunning</c>.</summary>
    bool ScriptRunning { get; }

    /// <summary>True when the file still exists.</summary>
    bool ScriptExists(string fullPath);

    /// <summary>Skua's <c>SetLoadedScript</c>.</summary>
    void SetLoadedScript(string fullPath);

    /// <summary>Skua's <c>StartScript</c>: null when the script started, the error otherwise.</summary>
    Task<Exception?> StartScript();

    /// <summary>True when the script just started declares options (read after the start).</summary>
    bool LoadedScriptHasOptions();

    /// <summary>Skua's <c>StopScript</c>; may take tens of seconds.</summary>
    Task StopScript();
}

/// <summary>
/// Turns each command from SkuaLink into exactly one <see cref="ResultMessage"/> with the command's <c>reqId</c>. Commands
/// run one at a time (a start's <see cref="SkuaControlState.CanStart"/> check and its
/// <see cref="SkuaControlState.OnStartAccepted"/> are separate steps), and nothing a command or Skua does escapes as an
/// exception: any failure is <c>ok: false</c> with a short error text. Pure: Skua, the folder and the clock are injected.
/// </summary>
public sealed class SkuaCommandHandler
{
    /// <summary>An accepted start that Skua has not reported running after this long settles (<see cref="SkuaControlState.OnStartNotSeen"/>).</summary>
    public static readonly TimeSpan StartSeenWait = TimeSpan.FromSeconds(10);

    private readonly ISkuaScriptHost _host;
    private readonly SkuaScriptCatalog _catalog;
    private readonly SkuaControlState _control;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private DateTimeOffset? _acceptedAt;
    private Task _stop = Task.CompletedTask;

    public SkuaCommandHandler(ISkuaScriptHost host, SkuaScriptCatalog catalog, SkuaControlState control, Func<DateTimeOffset> now)
    {
        _host = host;
        _catalog = catalog;
        _control = control;
        _now = now;
    }

    /// <summary>The stop Skua is working on; completed when there is none.</summary>
    public Task PendingStop
    {
        get
        {
            lock (_lock)
            {
                return _stop;
            }
        }
    }

    /// <summary>Answers one command. Never throws; waits for the command before it to finish.</summary>
    public async Task<ResultMessage> HandleAsync(SkuaCommand command)
    {
        var reqId = command?.ReqId ?? "";
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Fail(reqId, SkuaText.ErrorText(exception));
        }

        try
        {
            return command switch
            {
                ListScriptsCommand c => List(c.ReqId),
                StartCommand c => await StartAsync(c).ConfigureAwait(false),
                StopCommand c => Stop(c.ReqId),
                GetOptionsCommand or SetOptionsCommand => Fail(reqId, "not supported"),
                _ => Fail(reqId, "unknown command"),
            };
        }
        catch (Exception exception)
        {
            return Fail(reqId, SkuaText.ErrorText(exception));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs on the plugin's status poll: reads Skua's running flag into the control state (an event may have been missed),
    /// settles an accepted start Skua never reported running within <see cref="StartSeenWait"/>, and lets time pass. Never throws.
    /// </summary>
    public void Tick()
    {
        try
        {
            _control.OnScriptRunningChanged(_host.ScriptRunning);
        }
        catch (Exception)
        {
            // Skua could not be read this time; the running events still feed the state.
        }

        try
        {
            bool due;
            lock (_lock)
            {
                due = _acceptedAt is { } at && _now() - at >= StartSeenWait;
                if (due)
                {
                    _acceptedAt = null;
                }
            }

            if (due)
            {
                _control.OnStartNotSeen();
            }

            _control.Tick();
        }
        catch (Exception)
        {
            // The next poll tries again.
        }
    }

    private ResultMessage List(string reqId)
    {
        IReadOnlyList<SkuaScriptInfo> scripts;
        try
        {
            scripts = _catalog.Scan();
        }
        catch (Exception)
        {
            // The message may hold the local folder; the answer stays generic.
            return Fail(reqId, "could not list scripts");
        }

        return new ResultMessage(reqId, true, null, scripts, null, null, false, false, _catalog.Truncated);
    }

    private async Task<ResultMessage> StartAsync(StartCommand command)
    {
        var reqId = command.ReqId;

        // Never load another script while one runs: Skua restarts the loaded path on its own (restart, relogin).
        if (_host.ScriptRunning)
        {
            return Fail(reqId, "already running");
        }

        if (!_control.CanStart(out var refused))
        {
            return Fail(reqId, refused);
        }

        if (!Resolve(command.ScriptId, out var path))
        {
            return Fail(reqId, "unknown script id");
        }

        if (!_host.ScriptExists(path))
        {
            return Fail(reqId, "not found");
        }

        _host.SetLoadedScript(path);
        if (await _host.StartScript().ConfigureAwait(false) is { } error)
        {
            return Fail(reqId, SkuaText.ErrorText(error));
        }

        bool hasOptions;
        try
        {
            hasOptions = _host.LoadedScriptHasOptions();
        }
        catch (Exception)
        {
            hasOptions = false;
        }

        lock (_lock)
        {
            _acceptedAt = _now();
        }

        _control.OnStartAccepted(hasOptions);
        return new ResultMessage(reqId, true, null, null, null, null, hasOptions, false);
    }

    /// <summary>An id from the last listing; an id the plugin has not listed yet (the plugin was reloaded) gets one new scan.</summary>
    private bool Resolve(string id, out string path)
    {
        if (_catalog.TryResolve(id, out path))
        {
            return true;
        }

        try
        {
            _catalog.Scan();
        }
        catch (Exception)
        {
            return false;
        }

        return _catalog.TryResolve(id, out path);
    }

    /// <summary>
    /// Answers at once: Skua's stop may take tens of seconds, longer than SkuaLink waits for an answer. The control state shows
    /// the progress (<c>stopping</c>, then <c>settling</c>). Skua is stopped on a pool thread, never the caller's (Skua's stop
    /// refuses to wait when called from the script's own thread), and only once at a time; a stop asked for again after
    /// Skua gave up on an unresponsive script stops again.
    /// </summary>
    private ResultMessage Stop(string reqId)
    {
        var running = _host.ScriptRunning;
        var state = _control.State;
        if (!running && state is not ("running" or "awaitingOptions" or "stopping"))
        {
            return Fail(reqId, "not running");
        }

        _control.OnStopRequested();
        if (running)
        {
            lock (_lock)
            {
                if (_stop.IsCompleted)
                {
                    _stop = Task.Run(StopSkuaAsync);
                }
            }
        }

        return new ResultMessage(reqId, true, null, null, null, null, false, false);
    }

    private async Task StopSkuaAsync()
    {
        try
        {
            await _host.StopScript().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Skua's stop failed; the running flag still tells the state what happened, and a new stop may be asked for.
        }
    }

    private static ResultMessage Fail(string reqId, string error) =>
        new(reqId, false, SkuaText.Cut(error), null, null, null, false, false);
}
