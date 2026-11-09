using System.ComponentModel;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Feed;

namespace AqwCompanion.App.Feed;

/// <summary>
/// The feed's source over the live app state. The snapshot is rebuilt when something it shows may have changed (the session, the
/// dashboard's view model, Hard Farms) and cached until then. <see cref="Changed"/> may be raised from any thread.
/// </summary>
public sealed class SessionFeedSource : IFeedSource, IDisposable
{
    private readonly CaptureSession _session;
    private readonly GameDataViewModel _gameData;
    private readonly HardFarmsViewModel _hardFarms;
    private readonly object _lock = new();
    private FeedSnapshot? _cached;

    public SessionFeedSource(CaptureSession session, GameDataViewModel gameData, HardFarmsViewModel hardFarms)
    {
        _session = session;
        _gameData = gameData;
        _hardFarms = hardFarms;
        session.Changed += OnSessionChanged;
        gameData.PropertyChanged += OnViewModelChanged;
        hardFarms.PropertyChanged += OnHardFarmsChanged;
    }

    public event EventHandler? Changed;

    public FeedSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                return _cached ??= FeedSnapshotBuilder.Build(_session, _gameData, _hardFarms);
            }
        }
    }

    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        _gameData.PropertyChanged -= OnViewModelChanged;
        _hardFarms.PropertyChanged -= OnHardFarmsChanged;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Invalidate();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();

    private void OnHardFarmsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HardFarmsViewModel.Summary))
        {
            Invalidate();
        }
    }

    private void Invalidate()
    {
        lock (_lock)
        {
            _cached = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
