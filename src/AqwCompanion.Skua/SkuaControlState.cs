namespace AqwCompanion.Skua;

/// <summary>
/// What the plugin may do with Skua's script right now (<see cref="SkuaProtocol.ControlStates"/>). A stop request moves to
/// <c>stopping</c>; when the script stops running the state is <c>settling</c> until the map has not changed for
/// <see cref="SettleTime"/> (each map change starts the wait again), then <c>idle</c>. A start with options waits in
/// <c>awaitingOptions</c> until the options dialog closes or the script is seen alive, never by time alone. Pure and
/// thread-safe: the clock is injected and every call takes one lock. Every transition (of the state or of
/// <see cref="OptionsStuck"/>) calls the optional <c>changed</c> callback once, after the lock is released, so the plugin
/// publishes a status at once instead of on its next poll; a call that changes nothing does not call it.
/// </summary>
public sealed class SkuaControlState
{
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(10);

    private readonly Func<DateTimeOffset> _now;
    private readonly Action? _changed;
    private readonly object _gate = new();
    private string _state = "idle";
    private bool _scriptRunning;
    private bool _optionsStuck;
    private DateTimeOffset _settleUntil;

    /// <param name="now">The clock.</param>
    /// <param name="changed">Called after each transition, outside the lock; it may read this state. Its exceptions are swallowed.</param>
    public SkuaControlState(Func<DateTimeOffset> now, Action? changed = null)
    {
        _now = now;
        _changed = changed;
    }

    /// <summary>One of <c>idle|running|stopping|settling|awaitingOptions</c>.</summary>
    public string State
    {
        get
        {
            string state;
            bool changed;
            lock (_gate)
            {
                changed = Advance();
                state = _state;
            }

            Notify(changed);
            return state;
        }
    }

    /// <summary>Whole seconds (rounded up) until the settle ends, while <c>settling</c>; null otherwise.</summary>
    public int? SettleSecondsLeft
    {
        get
        {
            int? left;
            bool changed;
            lock (_gate)
            {
                changed = Advance();
                left = _state == "settling" ? (int)Math.Ceiling((_settleUntil - _now()).TotalSeconds) : null;
            }

            Notify(changed);
            return left;
        }
    }

    /// <summary>True while <c>awaitingOptions</c> after the plugin could not close the options dialog.</summary>
    public bool OptionsStuck
    {
        get
        {
            lock (_gate)
            {
                return _optionsStuck;
            }
        }
    }

    /// <summary>Skua's <c>ScriptRunning</c> changed (a repeated value changes nothing).</summary>
    public void OnScriptRunningChanged(bool running) => Change(() => ApplyRunning(running));

    /// <summary>
    /// Reads Skua's <c>ScriptRunning</c> inside the state's lock and applies it like <see cref="OnScriptRunningChanged"/>:
    /// two readers (the running event and the poll) are applied in the order they read, so an older read can never
    /// overwrite a newer one. A read that throws changes nothing and is passed on. The read must be cheap and must not
    /// call back into this state.
    /// </summary>
    public void OnScriptRunningRead(Func<bool> read) => Change(() => ApplyRunning(read()));

    private void ApplyRunning(bool running)
    {
        Advance();
        if (running == _scriptRunning)
        {
            return;
        }

        _scriptRunning = running;
        if (running)
        {
            if (_state is "idle" or "settling")
            {
                _state = "running";
            }
        }
        else
        {
            StartSettling();
        }
    }

    /// <summary>The player's map changed; while <c>settling</c> the wait starts again.</summary>
    public void OnMapChanged() => Change(() =>
    {
        Advance();
        if (_state == "settling")
        {
            _settleUntil = _now() + SettleTime;
        }
    });

    /// <summary>A stop was asked for; only a running script (or one awaiting its options) moves to <c>stopping</c>.</summary>
    public void OnStopRequested() => Change(() =>
    {
        Advance();
        if (_state is "running" or "awaitingOptions" || (_scriptRunning && _state != "stopping"))
        {
            _state = "stopping";
            _optionsStuck = false;
        }
    });

    /// <summary>Skua accepted a start; a script with options waits for its options dialog.</summary>
    public void OnStartAccepted(bool hasOptions) => Change(() =>
    {
        _state = hasOptions ? "awaitingOptions" : "running";
        _optionsStuck = false;
    });

    /// <summary>
    /// An accepted start was not seen running in time (Skua's running report may come and go before the start is accepted, or
    /// never come): when no script runs, a <c>running</c> or <c>awaitingOptions</c> state settles, so it never sticks.
    /// </summary>
    public void OnStartNotSeen() => Change(() =>
    {
        Advance();
        if (_state is "running" or "awaitingOptions" && !_scriptRunning)
        {
            StartSettling();
        }
    });

    /// <summary>The options dialog closed; the script runs with the saved options.</summary>
    public void OnOptionsDialogClosed() => LeaveAwaitingOptions();

    /// <summary>The script was seen running past its options.</summary>
    public void OnScriptAlive() => LeaveAwaitingOptions();

    /// <summary>The options dialog is still open after the plugin tried to close it.</summary>
    public void OnOptionsStuck() => Change(() =>
    {
        if (_state == "awaitingOptions")
        {
            _optionsStuck = true;
        }
    });

    /// <summary>Lets time pass: ends a settle that is over, and settles a stop whose script is no longer running.</summary>
    public void Tick() => Change(() =>
    {
        Advance();
        if (_state == "stopping" && !_scriptRunning)
        {
            StartSettling();
        }
    });

    /// <summary>True only when <c>idle</c> with no script running; otherwise the reason as the error text.</summary>
    public bool CanStart(out string error)
    {
        bool changed;
        lock (_gate)
        {
            changed = Advance();
            error = _state switch
            {
                "stopping" => "stopping",
                "settling" => "settling",
                "idle" when !_scriptRunning => "",
                _ => "already running",
            };
        }

        Notify(changed);
        return error.Length == 0;
    }

    private void LeaveAwaitingOptions() => Change(() =>
    {
        if (_state == "awaitingOptions")
        {
            _state = "running";
            _optionsStuck = false;
        }
    });

    /// <summary>Runs one step under the lock and calls back, outside it, when the state or the stuck flag moved.</summary>
    private void Change(Action step)
    {
        bool changed;
        lock (_gate)
        {
            var before = (_state, _optionsStuck);
            step();
            changed = before != (_state, _optionsStuck);
        }

        Notify(changed);
    }

    private void Notify(bool changed)
    {
        if (!changed || _changed is null)
        {
            return;
        }

        try
        {
            _changed();
        }
        catch (Exception)
        {
            // The caller (Skua's events, the poll, a command) must never see the publisher's failure.
        }
    }

    private void StartSettling()
    {
        _state = "settling";
        _optionsStuck = false;
        _settleUntil = _now() + SettleTime;
    }

    /// <summary>Ends a settle that is over; true when it did (the caller calls back after the lock).</summary>
    private bool Advance()
    {
        if (_state == "settling" && !_scriptRunning && _now() >= _settleUntil)
        {
            _state = "idle";
            return true;
        }

        return false;
    }
}
