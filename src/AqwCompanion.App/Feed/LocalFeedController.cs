using AqwCompanion.App.Settings;

namespace AqwCompanion.App.Feed;

/// <summary>Keeps the local feed in step with its Settings switch and tells the Settings dialog what is going on.</summary>
public sealed class LocalFeedController
{
    public const string OffText = "Off";

    private readonly SettingsViewModel _settings;
    private readonly ILocalFeedHost _host;
    private readonly int _port;
    private readonly FeedSecretStore? _secret;

    public LocalFeedController(SettingsViewModel settings, ILocalFeedHost host, int port = HttpListenerFeedHost.DefaultPort, FeedSecretStore? secret = null)
    {
        _secret = secret;
        _settings = settings;
        _host = host;
        _port = port;
        settings.LocalFeedChanged += (_, _) => Apply();
        settings.SetFeedStatus(OffText);
    }

    public string ListeningText => $"Listening on 127.0.0.1:{_port}";

    public string FailedText => $"Could not listen on 127.0.0.1:{_port} (the port may be in use)";

    /// <summary>Starts the feed when the saved setting is on; call once the app is up.</summary>
    public void Start() => Apply();

    private void Apply()
    {
        if (!_settings.LocalFeedEnabled)
        {
            _host.Stop();
            _settings.SetFeedStatus(OffText);
            return;
        }

        // The secret file exists as soon as the feed is on, so a program can read it before its first request.
        _secret?.GetOrCreate();
        _settings.SetFeedStatus(_host.IsRunning || _host.TryStart() ? ListeningText : FailedText);
    }
}
