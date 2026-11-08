using AqwCompanion.App.Settings;

namespace AqwCompanion.App.Debug;

/// <summary>Keeps the debug window and the saved debug mode in step: the switch opens it, closing it turns the switch off.</summary>
public sealed class DebugModeController
{
    private readonly SettingsViewModel _settings;
    private readonly IDebugWindowHost _host;

    public DebugModeController(SettingsViewModel settings, IDebugWindowHost host)
    {
        _settings = settings;
        _host = host;
        settings.DebugModeChanged += (_, _) => Apply();
        host.Closed += (_, _) => _settings.DebugMode = false;
    }

    /// <summary>Opens the window when the saved mode is on; call once the main window exists.</summary>
    public void Start() => Apply();

    /// <summary>Turns the debug mode on or off, as the shortcut and the tray menu do.</summary>
    public void Toggle() => _settings.DebugMode = !_settings.DebugMode;

    private void Apply()
    {
        if (_settings.DebugMode)
        {
            _host.Show();
        }
        else
        {
            _host.Close();
        }
    }
}
