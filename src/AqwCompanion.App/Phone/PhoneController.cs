using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using Serilog;

namespace AqwCompanion.App.Phone;

/// <summary>
/// Keeps the phone host, its notice tick and the pairing registry in step with the Settings switch, which lives in settings.json
/// and so survives "Forget my data". Also takes part in Forgetting: it revokes every device and clears the hub.
/// </summary>
public sealed class PhoneController : IForgettable, IDisposable
{
    public const string OffText = "Off";

    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly SettingsViewModel _settings;
    private readonly IPhoneHost _host;
    private readonly PhoneHub _hub;
    private readonly PhoneRegistry _registry;
    private readonly int _port;
    private readonly Func<TimeSpan, Action, IDisposable> _timerFactory;
    private readonly Action _tick;
    private readonly ITailscale _tailscale;
    private readonly IServeMarker _marker;
    private readonly Action<Action> _onUiThread;
    private readonly Func<Func<Task>, Task> _run;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _lock = new();
    private IDisposable? _timer;
    private bool _quiesced;
    private bool _published;
    private bool _stopPending;
    private bool _stopFailed;
    private string _tsText = string.Empty;
    private bool _tsCanPair;
    private TailscaleState? _state;

    // The tailnet name, set only after ServeAsync succeeded; read from request threads by AllowedHost.
    private volatile string? _servedName;

    /// <param name="onUiThread">Runs a change of the Settings view model on the UI thread (inline by default).</param>
    /// <param name="run">Starts the Tailscale work off the UI thread (<see cref="Task.Run(Func{Task})"/> by default).</param>
    public PhoneController(
        SettingsViewModel settings, IPhoneHost host, PhoneHub hub, PhoneRegistry registry, ITailscale tailscale, IServeMarker marker, int port = PhoneHost.DefaultPort,
        Func<TimeSpan, Action, IDisposable>? timerFactory = null, Action? tick = null,
        Action<Action>? onUiThread = null, Func<Func<Task>, Task>? run = null)
    {
        _tailscale = tailscale;
        _marker = marker;
        // A flag left by an earlier run (a crash, a failed removal) means a route may still be published.
        _published = marker.Exists();
        _onUiThread = onUiThread ?? (action => action());
        _run = run ?? (work => Task.Run(work));
        _settings = settings;
        _host = host;
        _hub = hub;
        _registry = registry;
        _port = port;
        _timerFactory = timerFactory ?? CreateTimer;
        _tick = tick ?? hub.Tick;
        settings.PhoneAccessChanged += (_, _) => Apply();
        settings.PhoneRefreshRequested += (_, _) => Reconcile();
        settings.SetPhoneStatus(OffText);
    }

    public string Name => "Phone access";

    public const string StopFailedText = "Could not remove the phone page from Tailscale. It will be tried again.";

    public const string ServeFailedText = "Could not publish the phone page through Tailscale. Check that Tailscale is running and try again.";

    /// <summary>What Tailscale offered the last time it was asked; null before that.</summary>
    public TailscaleState? TailscaleState
    {
        get { lock (_lock) { return _state; } }
    }

    /// <summary>
    /// Whether a request's Host header may be served: this PC's own address, or the tailnet name once it was published
    /// (Tailscale terminates TLS and forwards the bare name, so no port). Exact match, case-insensitive.
    /// </summary>
    public bool AllowedHost(string host) =>
        string.Equals(host, $"127.0.0.1:{_port}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, $"localhost:{_port}", StringComparison.OrdinalIgnoreCase)
        || (_servedName is { } name && string.Equals(host, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A one-time pairing link for the phone, or null unless the host runs and the page is published through Tailscale.</summary>
    public string? CreatePairingLink()
    {
        string name;
        lock (_lock)
        {
            if (!_host.IsRunning || _state?.Kind != TailscaleStateKind.Ready || _servedName is not { } served)
            {
                return null;
            }

            name = served;
        }

        try
        {
            return PairingUrl.Build(name, _registry.CreatePairingToken());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public string ListeningText => $"Listening on 127.0.0.1:{_port}";

    public string FailedText => $"Could not listen on 127.0.0.1:{_port} (the port may be in use, or it is not allowed)";

    /// <summary>Applies the saved switch; call once the app is up.</summary>
    public void Start() => Apply();

    public void Quiesce()
    {
        lock (_lock)
        {
            _quiesced = true;
            StopRunning();
            _settings.SetPhoneStatus(OffText);
            SetTailscaleText(string.Empty, false);
        }

        Reconcile();
    }

    public void Resume()
    {
        lock (_lock)
        {
            _quiesced = false;
            Apply();
        }
    }

    /// <summary>
    /// Revokes every device and clears the hub, then brings the host back as the saved switch says (with no devices every request is refused).
    /// The coordinator calls <see cref="Resume"/> only when the removal failed, so this must end the quiesced state itself.
    /// </summary>
    public void Forget()
    {
        _registry.Forget();
        _hub.Reset();
        lock (_lock)
        {
            _quiesced = false;
            _servedName = null;
            _stopPending = _published;
            Apply();
        }
    }

    /// <summary>Stops the tick and the host; the host and the hub themselves belong to the container.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _quiesced = true;
            StopRunning();
        }

        var pending = Reconcile();
        _cancel.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            // The published route outlives the process, so give the removal a short, bounded time to finish.
            pending.Wait(DisposeWait);
        }
        catch (Exception)
        {
            // Never throws; a failure was logged by the reconcile and the marker stays for the next start.
        }
    }

    private void Apply()
    {
        lock (_lock)
        {
            var enabled = _settings.PhoneAccessEnabled;
            if (_registry.Enabled != enabled)
            {
                _registry.SetEnabled(enabled);
            }

            if (_quiesced)
            {
                return;
            }

            if (!enabled)
            {
                StopRunning();
                _settings.SetPhoneStatus(OffText);
                SetTailscaleText(string.Empty, false);
                Reconcile();
                return;
            }

            if (_host.IsRunning || _host.TryStart())
            {
                _timer ??= _timerFactory(TickInterval, SafeTick);
                _settings.SetPhoneStatus(ListeningText);
                Reconcile();
            }
            else
            {
                StopRunning();
                _settings.SetPhoneStatus(FailedText);
                SetTailscaleText(string.Empty, false);
                Reconcile();
            }
        }
    }

    private void StopRunning()
    {
        _timer?.Dispose();
        _timer = null;
        _host.Stop();
        _servedName = null;
    }

    private bool Wanted() => _settings.PhoneAccessEnabled && !_quiesced && _host.IsRunning;

    /// <summary>Remembers what Settings should say and puts it on the UI thread; the text is judged again there, so a switch-off in between cannot leave "ready" on screen.</summary>
    private void SetTailscaleText(string text, bool canPair)
    {
        lock (_lock)
        {
            _tsText = text;
            _tsCanPair = canPair;
        }

        _onUiThread(() =>
        {
            string shown;
            bool pair;
            lock (_lock)
            {
                if (!Wanted() && !_stopFailed)
                {
                    shown = string.Empty;
                    pair = false;
                }
                else
                {
                    shown = _tsText;
                    pair = _tsCanPair && _servedName is not null && Wanted();
                }
            }

            _settings.SetPhoneTailscale(shown, pair);
        });
    }

    /// <summary>Brings the published address in step with the switch, off the UI thread. Every change of wish calls it; runs are serialised and read the wish when they run.</summary>
    private Task Reconcile()
    {
        try
        {
            return _run(ReconcileAsync);
        }
        catch (Exception ex)
        {
            Log.Warning("Phone Tailscale work failed: {ExceptionType}", ex.GetType().Name);
            return Task.CompletedTask;
        }
    }

    private async Task<bool> TryStopAsync(CancellationToken ct)
    {
        try
        {
            return await _tailscale.StopServeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning("Phone Tailscale work failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private async Task ReconcileAsync()
    {
        var entered = false;
        try
        {
            await _gate.WaitAsync(_cancel.Token).ConfigureAwait(false);
            entered = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var ct = timeout.Token;

            bool wanted;
            bool stop;
            lock (_lock)
            {
                wanted = Wanted();
                stop = _stopPending || (!wanted && _published);
                _stopPending = false;
                if (stop)
                {
                    _servedName = null;
                }
            }

            if (stop)
            {
                if (await TryStopAsync(ct).ConfigureAwait(false))
                {
                    lock (_lock)
                    {
                        _published = false;
                        _stopFailed = false;
                    }

                    _marker.Delete();
                    if (!wanted)
                    {
                        SetTailscaleText(string.Empty, false);
                    }
                }
                else
                {
                    // Still published: keep the flag so the next Apply, Refresh or start tries again.
                    lock (_lock)
                    {
                        _stopFailed = true;
                    }

                    SetTailscaleText(StopFailedText, false);
                    if (!wanted)
                    {
                        return;
                    }
                }
            }

            if (!wanted)
            {
                return;
            }

            var state = await _tailscale.GetStateAsync(ct).ConfigureAwait(false);
            var name = state.Kind == TailscaleStateKind.Ready ? TailscaleStatusParser.NormalizeDnsName(state.DnsName) : null;
            string text;
            bool canPair;
            bool needServe;
            bool staleRoute;
            lock (_lock)
            {
                _state = state;
                if (name is null || !string.Equals(_servedName, name, StringComparison.Ordinal))
                {
                    _servedName = null;
                }

                needServe = name is not null && _servedName is null && Wanted();
                staleRoute = name is null && _published && !_stopFailed;
                text = name is null ? state.Text : string.Empty;
                canPair = !needServe && _servedName is not null;
                if (canPair)
                {
                    text = state.Text;
                }
            }

            if (staleRoute && await TryStopAsync(ct).ConfigureAwait(false))
            {
                lock (_lock)
                {
                    _published = false;
                }

                _marker.Delete();
            }

            if (needServe)
            {
                // The flag goes down before the route does, so a crash in between still gets cleaned up.
                lock (_lock)
                {
                    _published = true;
                }

                _marker.Create();
                var ok = await _tailscale.ServeAsync(_port, ct).ConfigureAwait(false);
                lock (_lock)
                {
                    // A newer wish (switched off, quiesced) is handled by the run it queued; only record success while still wanted.
                    if (ok)
                    {
                        _stopFailed = false;
                        if (Wanted())
                        {
                            _servedName = name;
                        }
                    }
                }

                canPair = ok;
                text = ok ? state.Text : ServeFailedText;
            }

            lock (_lock)
            {
                if (_stopFailed)
                {
                    text = StopFailedText;
                    canPair = false;
                }
            }

            SetTailscaleText(text, canPair);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _servedName = null;
            }

            Log.Warning("Phone Tailscale work failed: {ExceptionType}", ex.GetType().Name);
            if (ex is not ObjectDisposedException)
            {
                SetTailscaleText(string.Empty, false);
            }
        }
        finally
        {
            if (entered)
            {
                _gate.Release();
            }
        }
    }

    private void SafeTick()
    {
        try
        {
            _tick();
        }
        catch (Exception ex)
        {
            Log.Warning("Phone tick failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    private static IDisposable CreateTimer(TimeSpan interval, Action tick) => new Timer(_ => tick(), null, interval, interval);
}
