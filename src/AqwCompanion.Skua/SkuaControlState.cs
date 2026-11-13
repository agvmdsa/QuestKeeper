namespace AqwCompanion.Skua;

/// <summary>
/// What the plugin may do with Skua's script right now (<see cref="SkuaProtocol.ControlStates"/>). A stop request moves to
/// <c>stopping</c>; when the script stops running the state is <c>settling</c> until the map has not changed for
/// <see cref="SettleTime"/> (each map change starts the wait again), then <c>idle</c>. A start with options waits in
/// <c>awaitingOptions</c> until the options dialog closes or the script is seen alive, never by time alone. Pure and
/// thread-safe: the clock is injected and every call takes one lock.
/// </summary>
public sealed class SkuaControlState
{
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(10);

    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private string _state = "idle";
    private bool _scriptRunning;
    private bool _optionsStuck;
    private DateTimeOffset _settleUntil;

    public SkuaControlState(Func<DateTimeOffset> now) => _now = now;

    /// <summary>One of <c>idle|running|stopping|settling|awaitingOptions</c>.</summary>
    public string State
    {
        get
        {
            lock (_gate)
            {
                Advance();
                return _state;
            }
        }
    }

    /// <summary>Whole seconds (rounded up) until the settle ends, while <c>settling</c>; null otherwise.</summary>
    public int? SettleSecondsLeft
    {
        get
        {
            lock (_gate)
            {
                Advance();
                return _state == "settling" ? (int)Math.Ceiling((_settleUntil - _now()).TotalSeconds) : null;
            }
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
    public void OnScriptRunningChanged(bool running)
    {
        lock (_gate)
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
    }

    /// <summary>The player's map changed; while <c>settling</c> the wait starts again.</summary>
    public void OnMapChanged()
    {
        lock (_gate)
        {
            Advance();
            if (_state == "settling")
            {
                _settleUntil = _now() + SettleTime;
            }
        }
    }

    /// <summary>A stop was asked for; only a running script (or one awaiting its options) moves to <c>stopping</c>.</summary>
    public void OnStopRequested()
    {
        lock (_gate)
        {
            Advance();
            if (_state is "running" or "awaitingOptions" || (_scriptRunning && _state != "stopping"))
            {
                _state = "stopping";
                _optionsStuck = false;
            }
        }
    }

    /// <summary>Skua accepted a start; a script with options waits for its options dialog.</summary>
    public void OnStartAccepted(bool hasOptions)
    {
        lock (_gate)
        {
            _state = hasOptions ? "awaitingOptions" : "running";
            _optionsStuck = false;
        }
    }

    /// <summary>The options dialog closed; the script runs with the saved options.</summary>
    public void OnOptionsDialogClosed() => LeaveAwaitingOptions();

    /// <summary>The script was seen running past its options.</summary>
    public void OnScriptAlive() => LeaveAwaitingOptions();

    /// <summary>The options dialog is still open after the plugin tried to close it.</summary>
    public void OnOptionsStuck()
    {
        lock (_gate)
        {
            if (_state == "awaitingOptions")
            {
                _optionsStuck = true;
            }
        }
    }

    /// <summary>Lets time pass: ends a settle that is over, and settles a stop whose script is no longer running.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            Advance();
            if (_state == "stopping" && !_scriptRunning)
            {
                StartSettling();
            }
        }
    }

    /// <summary>True only when <c>idle</c> with no script running; otherwise the reason as the error text.</summary>
    public bool CanStart(out string error)
    {
        lock (_gate)
        {
            Advance();
            error = _state switch
            {
                "stopping" => "stopping",
                "settling" => "settling",
                "idle" when !_scriptRunning => "",
                _ => "already running",
            };
            return error.Length == 0;
        }
    }

    private void LeaveAwaitingOptions()
    {
        lock (_gate)
        {
            if (_state == "awaitingOptions")
            {
                _state = "running";
                _optionsStuck = false;
            }
        }
    }

    private void StartSettling()
    {
        _state = "settling";
        _optionsStuck = false;
        _settleUntil = _now() + SettleTime;
    }

    private void Advance()
    {
        if (_state == "settling" && !_scriptRunning && _now() >= _settleUntil)
        {
            _state = "idle";
        }
    }
}
