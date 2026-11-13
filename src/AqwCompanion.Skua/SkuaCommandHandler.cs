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
/// exception: any failure is <c>ok: false</c> with a short error text. Pure: Skua, the folder, the clock and the waits are
/// injected.
/// </summary>
/// <remarks>
/// Script options (with an <see cref="ISkuaOptionsHost"/>): the definitions of a script are learnt from Skua's
/// <c>Config</c> right after the plugin starts it, and kept for this plugin load with the script's options file. The first
/// start of a script with options (none learnt yet) is a discovery: the plugin waits for the options dialog, closes it,
/// stops the script at once and answers <c>firstRun</c> with the definitions, so the owner fills them in before the real
/// start. A later start answers at once and closes the dialog in the background: <c>awaitingOptions</c> ends when the
/// dialog is gone, or after <see cref="OptionsWindowWait"/> when no dialog appeared (Skua shows it once per script per
/// session, and not at all for a script that preconfigures itself); a dialog still open after
/// <see cref="OptionsStuckAfter"/> is reported stuck and left to the owner on the PC. A dialog that cannot be looked at is
/// never taken for "no dialog".
/// </remarks>
public sealed class SkuaCommandHandler
{
    /// <summary>An accepted start that Skua has not reported running after this long settles (<see cref="SkuaControlState.OnStartNotSeen"/>).</summary>
    public static readonly TimeSpan StartSeenWait = TimeSpan.FromSeconds(10);

    /// <summary>A start with options whose dialog has not appeared after this long runs without one.</summary>
    public static readonly TimeSpan OptionsWindowWait = TimeSpan.FromSeconds(5);

    /// <summary>A dialog still open this long after the start is stuck (<see cref="SkuaControlState.OnOptionsStuck"/>).</summary>
    public static readonly TimeSpan OptionsStuckAfter = TimeSpan.FromSeconds(10);

    /// <summary>The same for a discovery start, which answers only after it, so the answer comes well within SkuaLink's 15 s.</summary>
    public static readonly TimeSpan FirstRunOptionsStuckAfter = TimeSpan.FromSeconds(8);

    /// <summary>How often the dialog is looked for until it is closed, and once stuck.</summary>
    public static readonly TimeSpan OptionsPoll = TimeSpan.FromMilliseconds(250);

    public static readonly TimeSpan StuckPoll = TimeSpan.FromSeconds(1);

    /// <summary>A window call that has not returned after this long (Skua's UI thread busy) counts as not seen.</summary>
    public static readonly TimeSpan WindowCallTimeout = TimeSpan.FromSeconds(2);

    private readonly ISkuaScriptHost _host;
    private readonly SkuaScriptCatalog _catalog;
    private readonly SkuaControlState _control;
    private readonly Func<DateTimeOffset> _now;
    private readonly ISkuaOptionsHost? _options;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<string, KnownOptions> _known = [];
    private DateTimeOffset? _acceptedAt;
    private Task _stop = Task.CompletedTask;
    private bool _stopIssued;
    private Task _optionsWatch = Task.CompletedTask;
    private int _startCount;
    private Task _windowCall = Task.CompletedTask;

    public SkuaCommandHandler(
        ISkuaScriptHost host, SkuaScriptCatalog catalog, SkuaControlState control, Func<DateTimeOffset> now,
        ISkuaOptionsHost? options = null, Func<TimeSpan, Task>? delay = null)
    {
        _host = host;
        _catalog = catalog;
        _control = control;
        _now = now;
        _options = options;
        _delay = delay ?? (time => Task.Delay(time));
    }

    /// <summary>The background close of an options dialog after a start; completed when there is none.</summary>
    public Task PendingOptions
    {
        get
        {
            lock (_lock)
            {
                return _optionsWatch;
            }
        }
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
                GetOptionsCommand c => GetOptions(c),
                SetOptionsCommand c => SetOptions(c),
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
    /// Runs on the plugin's status poll: reads Skua's running flag into the control state (an event may have been missed;
    /// the read happens inside the state's lock, so it cannot overwrite a newer read), stops a script that began running
    /// after its stop was accepted, settles an accepted start Skua never reported running within
    /// <see cref="StartSeenWait"/>, and lets time pass. Never throws.
    /// </summary>
    public void Tick()
    {
        try
        {
            _control.OnScriptRunningRead(() => _host.ScriptRunning);
        }
        catch (Exception)
        {
            // Skua could not be read this time; the running events still feed the state.
        }

        try
        {
            StopIfStillRunning();
        }
        catch (Exception)
        {
            // The next poll tries again.
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

        // A discovery only for a script with neither learnt definitions nor an options file (one from an earlier Skua
        // session means the owner has configured it: it starts the normal way, with the saved values).
        bool firstRun;
        lock (_lock)
        {
            firstRun = _options is not null && !_known.ContainsKey(command.ScriptId);
        }

        if (firstRun && (ReadFileOptions(path, out var unreadable) is not null || unreadable))
        {
            firstRun = false;
        }

        _host.SetLoadedScript(path);
        if (await _host.StartScript().ConfigureAwait(false) is { } error)
        {
            return Fail(reqId, SkuaText.ErrorText(error));
        }

        // Skua loads the script's options while starting it, so they are read now; they describe the script just started.
        var known = LearnOptions(command.ScriptId, path);
        bool hasOptions;
        if (known is not null)
        {
            hasOptions = known.Defs.Count > 0;
        }
        else
        {
            try
            {
                hasOptions = _host.LoadedScriptHasOptions();
            }
            catch (Exception)
            {
                hasOptions = false;
            }
        }

        lock (_lock)
        {
            _acceptedAt = _now();
            _startCount++;
        }

        _control.OnStartAccepted(hasOptions);
        if (!hasOptions || _options is null)
        {
            return new ResultMessage(reqId, true, null, null, known?.Defs, null, hasOptions, false);
        }

        if (firstRun && known is not null)
        {
            return await DiscoverAsync(reqId, known).ConfigureAwait(false);
        }

        var watch = new DialogWatch(this, OptionsStuckAfter);
        lock (_lock)
        {
            _optionsWatch = Task.Run(() => WatchOptionsAsync(watch));
        }

        return new ResultMessage(reqId, true, null, null, known?.Defs, null, true, false);
    }

    /// <summary>
    /// The first start of a script with options: waits for its dialog, closes it, then stops the script at once, so it
    /// never runs with options the owner has not seen. A dialog that cannot be closed is left to the owner, loudly, and the
    /// script is not stopped (stopping would leave the dialog on the PC with nothing to show for it on the phone).
    /// </summary>
    private async Task<ResultMessage> DiscoverAsync(string reqId, KnownOptions known)
    {
        var watch = new DialogWatch(this, FirstRunOptionsStuckAfter);
        DialogOutcome outcome;
        while ((outcome = await watch.StepAsync().ConfigureAwait(false)) == DialogOutcome.Pending)
        {
            await _delay(OptionsPoll).ConfigureAwait(false);
        }

        if (outcome == DialogOutcome.Stuck)
        {
            _control.OnOptionsStuck();
            lock (_lock)
            {
                _optionsWatch = Task.Run(() => WatchOptionsAsync(watch));
            }

            return new ResultMessage(reqId, true, null, null, known.Defs, null, true, false);
        }

        RequestStop(_host.ScriptRunning);
        return new ResultMessage(reqId, true, null, null, known.Defs, ReadSavedValues(known, out _) ?? [], true, true);
    }

    /// <summary>Runs in the background after a start: closes the dialog, then reports it closed, absent or stuck. Never throws.</summary>
    private async Task WatchOptionsAsync(DialogWatch watch)
    {
        try
        {
            while (true)
            {
                // A newer start has its own watch; this one must not act on that start's dialog or state.
                if (!watch.IsCurrent)
                {
                    return;
                }

                var outcome = await watch.StepAsync().ConfigureAwait(false);
                switch (outcome)
                {
                    case DialogOutcome.Closed:
                        _control.OnOptionsDialogClosed();
                        return;
                    case DialogOutcome.NoWindow:
                        _control.OnScriptAlive();
                        return;
                    case DialogOutcome.Stuck:
                        // Then only watched (no more closing) until the owner closes it or this start's wait ends.
                        _control.OnOptionsStuck();
                        if (_control.State != "awaitingOptions" || !watch.IsCurrent)
                        {
                            return;
                        }

                        await _delay(StuckPoll).ConfigureAwait(false);
                        break;
                    default:
                        await _delay(OptionsPoll).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception)
        {
            // Never into Skua; the state still ends on a stop or a start that is not seen.
        }
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

        RequestStop(running);
        return new ResultMessage(reqId, true, null, null, null, null, false, false);
    }

    /// <summary>
    /// Moves to <c>stopping</c> and stops Skua when it reports the script running; otherwise <see cref="Tick"/> stops it
    /// once it does (a start accepted but not reported yet must not run on after an accepted stop).
    /// </summary>
    private void RequestStop(bool running)
    {
        _control.OnStopRequested();
        lock (_lock)
        {
            _stopIssued = running;
            if (running && _stop.IsCompleted)
            {
                _stop = Task.Run(StopSkuaAsync);
            }
        }
    }

    /// <summary>While <c>stopping</c>, a script Skua reports running with no stop issued for it yet is stopped, once.</summary>
    private void StopIfStillRunning()
    {
        if (_control.State != "stopping" || !_host.ScriptRunning)
        {
            return;
        }

        lock (_lock)
        {
            if (_stopIssued || !_stop.IsCompleted)
            {
                return;
            }

            _stopIssued = true;
            _stop = Task.Run(StopSkuaAsync);
        }
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

    // ---- options ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The script's definitions and saved values. Definitions not learnt yet (the plugin has not started the script since
    /// it loaded) answer <c>ok</c> with no definitions: the next start is a discovery. An empty list means no options.
    /// </summary>
    private ResultMessage GetOptions(GetOptionsCommand command)
    {
        var reqId = command.ReqId;
        if (_options is null)
        {
            return Fail(reqId, "not supported");
        }

        if (!Resolve(command.ScriptId, out var path))
        {
            return Fail(reqId, "unknown script id");
        }

        if (Known(command.ScriptId, path) is not { } known)
        {
            // Not learnt in this plugin load: the values of an options file from an earlier session, without definitions.
            if (ReadFileOptions(path, out var unreadable) is { } file)
            {
                return new ResultMessage(reqId, true, null, null, null, file.Values, file.Values.Count > 0, false);
            }

            return unreadable
                ? Fail(reqId, "could not read options")
                : new ResultMessage(reqId, true, null, null, null, null, false, false);
        }

        if (ReadSavedValues(known, out var failed) is not { } values)
        {
            return failed ? Fail(reqId, "could not read options") : new ResultMessage(reqId, true, null, null, known.Defs, [], known.Defs.Count > 0, false);
        }

        return new ResultMessage(reqId, true, null, null, known.Defs, values, known.Defs.Count > 0, false);
    }

    /// <summary>
    /// Validates the values against the learnt definitions and writes the script's options file in Skua's format (the
    /// saved values with these applied). Only while no script runs: Skua reads the file at the start, and an open dialog
    /// would overwrite it when it closes.
    /// </summary>
    private ResultMessage SetOptions(SetOptionsCommand command)
    {
        var reqId = command.ReqId;
        if (_options is null)
        {
            return Fail(reqId, "not supported");
        }

        if (!Resolve(command.ScriptId, out var path))
        {
            return Fail(reqId, "unknown script id");
        }

        var state = _control.State;
        if (_host.ScriptRunning || state is "running" or "awaitingOptions")
        {
            return Fail(reqId, "already running");
        }

        if (state == "stopping")
        {
            return Fail(reqId, "stopping");
        }

        var changes = command.Values ?? [];
        if (Known(command.ScriptId, path) is not { } known)
        {
            return SetFileOptions(reqId, path, changes);
        }

        if (SkuaOptionsFile.Validate(known.Defs, changes) is { } invalid)
        {
            return Fail(reqId, invalid);
        }

        if (!SkuaScriptOptions.IsOptionsFile(_options.OptionsRoot, known.OptionsFile))
        {
            return Fail(reqId, "options file not allowed");
        }

        if (ReadSavedValues(known, out _) is not { } saved)
        {
            return Fail(reqId, "could not read options");
        }

        var merged = SkuaScriptOptions.Merge(known.Defs, saved, changes);
        try
        {
            _options.WriteOptionsFile(known.OptionsFile!, SkuaOptionsFile.Format(SkuaScriptOptions.ForFile(known.Defs, merged, _options.FileCulture)));
        }
        catch (Exception)
        {
            // The message may hold the local folder; the answer stays generic.
            return Fail(reqId, "could not write options");
        }

        return new ResultMessage(reqId, true, null, null, known.Defs, merged, known.Defs.Count > 0, false);
    }

    /// <summary>
    /// <c>setOptions</c> without learnt definitions (Skua restarted since the plugin last started the script): only names
    /// already in the script's options file, checked with the rules that need no type (length, line break, trailing
    /// space); SkuaLink checks the types from the definitions it keeps. The file keeps its names and order.
    /// </summary>
    private ResultMessage SetFileOptions(string reqId, string path, IReadOnlyList<SkuaOptionValue> changes)
    {
        if (ReadFileOptions(path, out var unreadable) is not { } file)
        {
            return Fail(reqId, unreadable ? "could not read options" : "options not known yet");
        }

        // Every name in the file as an untyped option: Validate then refuses any other name and checks only the text.
        var names = SkuaScriptOptions.Untyped(file.Values);
        if (SkuaOptionsFile.Validate(names, changes) is { } invalid)
        {
            return Fail(reqId, invalid);
        }

        var merged = SkuaScriptOptions.Merge(names, file.Values, changes);
        try
        {
            _options!.WriteOptionsFile(file.OptionsFile, SkuaOptionsFile.Format(merged));
        }
        catch (Exception)
        {
            return Fail(reqId, "could not write options");
        }

        return new ResultMessage(reqId, true, null, null, null, merged, merged.Count > 0, false);
    }

    /// <summary>
    /// The script's options file found without Skua's <c>Config</c>: named by the <c>OptionsStorage</c> its source
    /// declares (<see cref="SkuaScriptOptions.StorageName"/>). Null when the name cannot be read from the source or there
    /// is no file; null with <paramref name="unreadable"/> when the file exists but cannot be read.
    /// </summary>
    private FileOptions? ReadFileOptions(string path, out bool unreadable)
    {
        unreadable = false;
        if (_options is null)
        {
            return null;
        }

        string? storage;
        try
        {
            storage = SkuaScriptOptions.StorageName(_options.ReadScriptSource(path));
        }
        catch (Exception)
        {
            return null;
        }

        if (storage is null)
        {
            return null;
        }

        var file = SkuaScriptOptions.OptionsFileFor(_options.OptionsRoot, storage);
        if (!SkuaScriptOptions.IsOptionsFile(_options.OptionsRoot, file))
        {
            return null;
        }

        try
        {
            return _options.ReadOptionsFile(file) is { } lines ? new FileOptions(file, SkuaOptionsFile.Parse(lines)) : null;
        }
        catch (Exception)
        {
            unreadable = true;
            return null;
        }
    }

    /// <summary>Reads Skua's options right after a start of this script and keeps them; null when they cannot be read.</summary>
    private KnownOptions? LearnOptions(string id, string path)
    {
        if (_options is null)
        {
            return null;
        }

        try
        {
            // Skua's Config belongs to the script it started last; a loaded path that differs means it is not this one.
            if (_options.ReadLoadedOptions() is not { } loaded
                || !string.Equals(loaded.ScriptPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var known = new KnownOptions(path, loaded.OptionsFile, SkuaScriptOptions.Definitions(loaded.Options ?? []));
            lock (_lock)
            {
                _known[id] = known;
            }

            return known;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private KnownOptions? Known(string id, string path)
    {
        lock (_lock)
        {
            return _known.TryGetValue(id, out var known) && known.Path == path ? known : null;
        }
    }

    /// <summary>
    /// The saved values of the script's known options: empty when there are no options, no file, or the file is not one
    /// the plugin may touch; null with <paramref name="failed"/> when the file cannot be read.
    /// </summary>
    private IReadOnlyList<SkuaOptionValue>? ReadSavedValues(KnownOptions known, out bool failed)
    {
        failed = false;
        if (_options is null || known.Defs.Count == 0 || !SkuaScriptOptions.IsOptionsFile(_options.OptionsRoot, known.OptionsFile))
        {
            return [];
        }

        try
        {
            var lines = _options.ReadOptionsFile(known.OptionsFile!);
            return lines is null ? [] : SkuaScriptOptions.SavedValues(known.Defs, SkuaOptionsFile.Parse(lines), _options.FileCulture);
        }
        catch (Exception)
        {
            failed = true;
            return null;
        }
    }

    /// <summary>
    /// The plugin's window code runs on Skua's UI thread: a busy UI thread must not hold the command worker (each call is
    /// waited for at most <see cref="WindowCallTimeout"/>), and while one call has not returned no other is made, so a
    /// blocked UI thread never piles up waiting threads.
    /// </summary>
    private async Task<T> WindowCallAsync<T>(Func<T> call, T unknown)
    {
        Task<T> task;
        lock (_lock)
        {
            if (!_windowCall.IsCompleted)
            {
                return unknown;
            }

            task = Task.Run(call);
            _windowCall = task;
        }

        try
        {
            return await task.WaitAsync(WindowCallTimeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return unknown;
        }
    }

    private Task<SkuaOptionsWindow> FindWindowAsync() => WindowCallAsync(_options!.FindOptionsWindow, SkuaOptionsWindow.Unknown);

    /// <summary>A close that fails is seen on the next look: still open, then stuck.</summary>
    private Task<bool> CloseWindowAsync() => WindowCallAsync(() =>
    {
        _options!.CloseOptionsWindow();
        return true;
    }, false);

    private int StartCount
    {
        get
        {
            lock (_lock)
            {
                return _startCount;
            }
        }
    }

    private sealed record FileOptions(string OptionsFile, IReadOnlyList<SkuaOptionValue> Values);

    private sealed record KnownOptions(string Path, string? OptionsFile, IReadOnlyList<SkuaOptionDef> Defs);

    private enum DialogOutcome
    {
        Pending,
        Closed,
        NoWindow,
        Stuck,
    }

    /// <summary>
    /// One start's options dialog. Each step looks once: gone after it was seen is <c>Closed</c>; never seen within
    /// <see cref="OptionsWindowWait"/> is <c>NoWindow</c>; open (or not lookable) at the stuck time is <c>Stuck</c>, after
    /// which it is only watched, never closed again; open before that is closed.
    /// </summary>
    private sealed class DialogWatch(SkuaCommandHandler owner, TimeSpan stuckAfter)
    {
        private readonly DateTimeOffset _started = owner._now();
        private readonly int _start = owner.StartCount;
        private bool _seen;
        private bool _stuck;

        /// <summary>False once another start was accepted after the one this watch belongs to.</summary>
        public bool IsCurrent => owner.StartCount == _start;

        public async Task<DialogOutcome> StepAsync()
        {
            var window = await owner.FindWindowAsync().ConfigureAwait(false);
            var elapsed = owner._now() - _started;
            if (window == SkuaOptionsWindow.None)
            {
                if (_seen || _stuck)
                {
                    return DialogOutcome.Closed;
                }

                return elapsed >= OptionsWindowWait ? DialogOutcome.NoWindow : DialogOutcome.Pending;
            }

            _seen |= window == SkuaOptionsWindow.Open;
            if (_stuck || elapsed >= stuckAfter)
            {
                _stuck = true;
                return DialogOutcome.Stuck;
            }

            if (window == SkuaOptionsWindow.Open)
            {
                await owner.CloseWindowAsync().ConfigureAwait(false);
            }

            return DialogOutcome.Pending;
        }
    }

    private static ResultMessage Fail(string reqId, string error) =>
        new(reqId, false, SkuaText.Cut(error), null, null, null, false, false);
}
