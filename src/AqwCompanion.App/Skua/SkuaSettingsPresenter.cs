using AqwCompanion.App.Settings;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.App.Skua;

/// <summary>Keeps the Skua status line of the Settings dialog in step with the bridge; changes are applied on the UI thread.</summary>
public sealed class SkuaSettingsPresenter : IDisposable
{
    private readonly ISkuaFeed _feed;
    private readonly SettingsViewModel _settings;
    private readonly Action<Action> _onUiThread;
    private bool _started;
    private bool _disposed;

    public SkuaSettingsPresenter(ISkuaFeed feed, SettingsViewModel settings, Action<Action> onUiThread)
    {
        _feed = feed;
        _settings = settings;
        _onUiThread = onUiThread;
    }

    /// <summary>Shows the current state and follows every change.</summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _feed.Changed += OnChanged;
        Apply();
    }

    public void Dispose()
    {
        _disposed = true;
        _feed.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Apply();

    private void Apply() => _onUiThread(() =>
    {
        if (!_disposed)
        {
            _settings.SetSkuaStatus(SkuaStatusText.For(_feed.Current));
        }
    });
}
