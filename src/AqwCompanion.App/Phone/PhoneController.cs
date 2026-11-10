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

    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly SettingsViewModel _settings;
    private readonly IPhoneHost _host;
    private readonly PhoneHub _hub;
    private readonly PhoneRegistry _registry;
    private readonly int _port;
    private readonly Func<TimeSpan, Action, IDisposable> _timerFactory;
    private readonly Action _tick;
    private readonly object _lock = new();
    private IDisposable? _timer;
    private bool _quiesced;

    public PhoneController(
        SettingsViewModel settings, IPhoneHost host, PhoneHub hub, PhoneRegistry registry, int port = PhoneHost.DefaultPort,
        Func<TimeSpan, Action, IDisposable>? timerFactory = null, Action? tick = null)
    {
        _settings = settings;
        _host = host;
        _hub = hub;
        _registry = registry;
        _port = port;
        _timerFactory = timerFactory ?? CreateTimer;
        _tick = tick ?? hub.Tick;
        settings.PhoneAccessChanged += (_, _) => Apply();
        settings.SetPhoneStatus(OffText);
    }

    public string Name => "Phone access";

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
        }
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
                return;
            }

            if (_host.IsRunning || _host.TryStart())
            {
                _timer ??= _timerFactory(TickInterval, SafeTick);
                _settings.SetPhoneStatus(ListeningText);
            }
            else
            {
                StopRunning();
                _settings.SetPhoneStatus(FailedText);
            }
        }
    }

    private void StopRunning()
    {
        _timer?.Dispose();
        _timer = null;
        _host.Stop();
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
