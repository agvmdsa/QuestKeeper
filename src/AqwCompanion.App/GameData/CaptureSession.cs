using AqwCompanion.App.Infrastructure.Elevation;
using AqwCompanion.App.SignIn;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Forgetting;

namespace AqwCompanion.App.GameData;

/// <summary>
/// One Capture session at a time: checks the system, Npcap and administrator rights, listens through a packet source until it is
/// stopped or the source ends, keeps one snapshot per Character (newest wins) and stores them. It logs nothing that comes
/// from the traffic. State is guarded by a lock; <see cref="Changed"/> is raised outside of it and may come from capture threads.
/// After a login, the live events of the connection replace the Character's snapshot; those updates are saved a few seconds after
/// the last one (through <see cref="IDelay"/>, one pending save at a time) and at once when the session stops.
/// </summary>
public sealed class CaptureSession(
    Func<IPacketSource> sourceFactory,
    IProcessElevation elevation,
    Func<NpcapStatus> npcap,
    Func<bool> isWindows,
    IGameDataStore store,
    IDelay delay,
    TimeProvider clock,
    IApplicationExit exit,
    AppPaths paths) : IForgettable
{
    /// <summary>The most Characters kept; the oldest is dropped first.</summary>
    public const int MaxCharacters = GameDataStore.MaxCharacters;

    /// <summary>How long after the last live update the snapshots are saved.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private CaptureState _state;
    private CaptureFailure _failure;
    private readonly HashSet<string> _capturedNames = new(StringComparer.OrdinalIgnoreCase);
    private CharacterSnapshot[] _characters = [];
    private CaptureEngine? _engine;
    private IPacketSource? _source;
    private Run? _current;
    private Run? _lastRun;
    private bool _quiesced;
    private bool _forgotten;
    private bool _restoreStarted;
    private int _epoch;
    private readonly Dictionary<string, int> _liveCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _liveAt = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;
    private bool _saving;
    private long _saveStamp;
    private CancellationTokenSource? _saveCts;

    /// <summary>Raised on every state change, every captured Character and every live update, never periodically. May come from any thread.</summary>
    public event EventHandler? Changed;

    public CaptureState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public CaptureFailure Failure
    {
        get
        {
            lock (_gate)
            {
                return _failure;
            }
        }
    }

    /// <summary>What the engine has counted so far; zero before the first start.</summary>
    public CaptureCounters Counters
    {
        get
        {
            CaptureEngine? engine;
            lock (_gate)
            {
                engine = _engine;
            }

            return engine?.Counters ?? default;
        }
    }

    /// <summary>How far the server data got so far (counts only); zero before the first start.</summary>
    public CaptureStageCounters Stages
    {
        get
        {
            CaptureEngine? engine;
            lock (_gate)
            {
                engine = _engine;
            }

            return engine?.Stages ?? default;
        }
    }

    /// <summary>Why messages were unreadable, which known commands were read and which unknown names were seen (counts only); all zero before the first start.</summary>
    public CaptureDecodeDiagnostics Decoding
    {
        get
        {
            CaptureEngine? engine;
            lock (_gate)
            {
                engine = _engine;
            }

            return engine?.Decoding ?? CaptureDecodeDiagnostics.Empty;
        }
    }

    /// <summary>How the connections went and the shapes of the first unreadable messages (counts and classes only); zero before the first start.</summary>
    public CaptureConnectionDiagnostics Connections
    {
        get
        {
            CaptureEngine? engine;
            lock (_gate)
            {
                engine = _engine;
            }

            return engine?.Connections ?? CaptureConnectionDiagnostics.Empty;
        }
    }

    /// <summary>
    /// What the packet source of the current or last run says about itself, read on access: numbers and exception type
    /// names only. Empty before a run, when the source has nothing to say, and after Forgetting.
    /// </summary>
    public string Details
    {
        get
        {
            IPacketSource? source;
            lock (_gate)
            {
                source = _source;
            }

            try
            {
                return (source as ICaptureDiagnostics)?.Describe() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>The numbers of the live reading after the logins; all zero before the first start.</summary>
    public CaptureLiveDiagnostics Live
    {
        get
        {
            CaptureEngine? engine;
            lock (_gate)
            {
                engine = _engine;
            }

            return engine?.Live ?? CaptureLiveDiagnostics.Empty;
        }
    }

    /// <summary>One snapshot per Character name (ignoring case); a newer capture replaces the older one.</summary>
    public IReadOnlyList<CharacterSnapshot> Characters
    {
        get
        {
            lock (_gate)
            {
                return _characters;
            }
        }
    }

    /// <summary>True when a Character of this name (ignoring case) was captured during this run of the app, not only restored from storage.</summary>
    public bool IsCapturedThisSession(string name)
    {
        lock (_gate)
        {
            return _capturedNames.Contains(name);
        }
    }

    /// <summary>True when the Character (ignoring case) has received at least one live update since it was captured.</summary>
    public bool IsLive(string name) => LiveUpdateCount(name) > 0;

    /// <summary>How many live updates the Character (ignoring case) received since it was captured; kept in memory only.</summary>
    public int LiveUpdateCount(string name)
    {
        lock (_gate)
        {
            return _liveCounts.GetValueOrDefault(name);
        }
    }

    /// <summary>When the Character (ignoring case) received its last live update; null when it received none. Kept in memory only.</summary>
    public DateTimeOffset? LastLiveUpdateUtc(string name)
    {
        lock (_gate)
        {
            return _liveAt.TryGetValue(name, out var at) ? at : null;
        }
    }

    string IForgettable.Name => "Game data";

    /// <summary>Loads the stored Characters once. Ignored after Forgetting or Quiescing started and when Characters already has data.</summary>
    public async Task RestoreAsync()
    {
        int epoch;
        lock (_gate)
        {
            if (_restoreStarted || _quiesced || _forgotten)
            {
                return;
            }

            _restoreStarted = true;
            epoch = _epoch;
        }

        var loaded = await Task.Run(store.Load).ConfigureAwait(false);

        var changed = false;
        lock (_gate)
        {
            if (epoch == _epoch && !_quiesced && !_forgotten && _characters.Length == 0 && loaded.Count > 0)
            {
                _characters = [.. loaded.Take(MaxCharacters)];
                changed = true;
            }
        }

        if (changed)
        {
            OnChanged();
        }
    }

    /// <summary>Runs one session and returns when it ends (stop, end of the source or failure). No-op when already listening or quiesced.</summary>
    public async Task StartAsync()
    {
        var run = Begin();
        if (run is null)
        {
            return;
        }

        try
        {
            await RunAsync(run).ConfigureAwait(false);
        }
        finally
        {
            run.Finished.TrySetResult();
        }
    }

    private async Task RunAsync(Run run)
    {
        var token = run.Cts.Token;
        var source = Task.Run(() =>
        {
            var packetSource = sourceFactory();
            lock (_gate)
            {
                if (_current == run)
                {
                    _source = packetSource;
                }
            }

            return packetSource.RunAsync(run.Engine.Accept, token);
        });
        await Settle(source).ConfigureAwait(false);

        var state = CaptureState.Done;
        var failure = CaptureFailure.None;
        if (source.IsFaulted)
        {
            state = CaptureState.Failed;
            failure = CaptureFailure.DriverError;
        }
        else if (!token.IsCancellationRequested
            && run.Engine.Counters is { SegmentsSeen: 0, CharactersCaptured: 0 })
        {
            state = CaptureState.Failed;
            failure = CaptureFailure.NoDevice;
        }

        var raise = false;
        lock (_gate)
        {
            if (_current == run)
            {
                _current = null;
                _state = state;
                _failure = failure;
                raise = true;
            }
        }

        Cancel(run.Cts);
        run.Cts.Dispose();
        FlushSave();
        if (raise)
        {
            OnChanged();
        }
    }

    /// <summary>Ends a listening session; the state becomes Done.</summary>
    public void Stop()
    {
        var changed = EndRun(CaptureState.Done);
        FlushSave();
        if (changed)
        {
            OnChanged();
        }
    }

    /// <summary>
    /// Stops the session and waits until the running <see cref="StartAsync"/> has finished (the devices are closed) or the
    /// timeout passes, whichever comes first. Never throws and never waits longer than the timeout, even when the source
    /// ignores the cancellation. Does nothing when no session runs.
    /// </summary>
    public async Task StopAndWaitAsync(TimeSpan timeout)
    {
        try
        {
            Stop();
            Task? finished;
            lock (_gate)
            {
                finished = _lastRun?.Finished.Task;
            }

            if (finished is null || finished.IsCompleted)
            {
                return;
            }

            await Task.WhenAny(finished, Task.Delay(timeout)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Closing must never fail because of the wait.
        }
    }

    /// <summary>Starts this program again as administrator and closes it when that worked.</summary>
    public bool RelaunchAsAdministrator()
    {
        var started = elevation.TryRelaunchElevated(paths.Root);
        if (started)
        {
            exit.Exit();
        }

        return started;
    }

    void IForgettable.Quiesce()
    {
        bool changed;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            _quiesced = true;
            _epoch++;
            changed = EndRunLocked(CaptureState.Done, out cts);
        }

        Cancel(cts);
        FlushSave();
        if (changed)
        {
            OnChanged();
        }
    }

    void IForgettable.Resume()
    {
        lock (_gate)
        {
            _quiesced = false;
        }
    }

    void IForgettable.Forget()
    {
        CancellationTokenSource? cts;
        CancellationTokenSource? saveCts;
        lock (_gate)
        {
            _epoch++;
            cts = _current?.Cts;
            _current = null;
            _characters = [];
            _capturedNames.Clear();
            _liveCounts.Clear();
            _liveAt.Clear();
            _dirty = false;
            _saving = false;
            saveCts = _saveCts;
            _saveCts = null;
            _engine = null;
            _source = null;
            _state = CaptureState.Idle;
            _failure = CaptureFailure.None;
            _quiesced = false;
            _forgotten = true;
        }

        Cancel(cts);
        Cancel(saveCts);
        OnChanged();
    }

    // Decides under the lock whether a session starts: an early state ends it at once, otherwise a run is created.
    private Run? Begin()
    {
        Run? run = null;
        lock (_gate)
        {
            if (_quiesced || _state == CaptureState.Listening)
            {
                return null;
            }

            _failure = CaptureFailure.None;
            if (Preflight() is { } early)
            {
                _state = early;
            }
            else
            {
                var engine = new CaptureEngine(clock);
                run = new Run(engine);
                engine.CharacterCaptured += snapshot => OnCaptured(run, snapshot);
                engine.CharacterUpdated += snapshot => OnUpdated(run, snapshot);
                _engine = engine;
                _source = null;
                _current = run;
                _lastRun = run;
                _state = CaptureState.Listening;
            }
        }

        OnChanged();
        return run;
    }

    private CaptureState? Preflight()
    {
        if (!isWindows())
        {
            return CaptureState.NotSupported;
        }

        switch (npcap())
        {
            case NpcapStatus.Missing:
                return CaptureState.NpcapMissing;
            case NpcapStatus.NotSupported:
                return CaptureState.NotSupported;
        }

        return elevation.IsElevated ? null : CaptureState.NeedsAdministrator;
    }

    private void OnCaptured(Run run, CharacterSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_current != run)
            {
                return;
            }

            var list = _characters
                .Where(c => !string.Equals(c.Profile.Name, snapshot.Profile.Name, StringComparison.OrdinalIgnoreCase))
                .Append(snapshot)
                .ToList();
            if (list.Count > MaxCharacters)
            {
                list.RemoveRange(0, list.Count - MaxCharacters);
            }

            _characters = [.. list];
            _capturedNames.Add(snapshot.Profile.Name);
            _liveCounts.Remove(snapshot.Profile.Name);
            _liveAt.Remove(snapshot.Profile.Name);
            _dirty = false;
            store.Save(_characters);
        }

        OnChanged();
    }

    // A live event changed the snapshot of a Character: replace it, count it and save it a few seconds after the last one.
    private void OnUpdated(Run run, CharacterSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_current != run)
            {
                return;
            }

            var index = Array.FindIndex(_characters, c => string.Equals(c.Profile.Name, snapshot.Profile.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            var name = _characters[index].Profile.Name;
            _characters = [.. _characters.Select((c, i) => i == index ? snapshot : c)];
            _liveCounts[name] = _liveCounts.GetValueOrDefault(name) + 1;
            _liveAt[name] = clock.GetUtcNow();
            _dirty = true;
            _saveStamp++;
            if (!_saving)
            {
                _saving = true;
                _saveCts = new CancellationTokenSource();
                var token = _saveCts.Token;
                var epoch = _epoch;
                _ = Task.Run(() => SaveAfterQuietAsync(epoch, token));
            }
        }

        OnChanged();
    }

    // Waits SaveDelay after the last update (more updates while waiting wait again), then saves once. A wait that was cancelled
    // (by a flush or by forgetting) ends without touching anything: whoever cancelled it settled the state.
    private async Task SaveAfterQuietAsync(int epoch, CancellationToken token)
    {
        try
        {
            while (true)
            {
                long stamp;
                lock (_gate)
                {
                    stamp = _saveStamp;
                }

                await delay.DelayAsync(SaveDelay, token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (token.IsCancellationRequested || epoch != _epoch)
                    {
                        return;
                    }

                    if (_saveStamp != stamp)
                    {
                        continue;
                    }

                    if (_dirty)
                    {
                        _dirty = false;
                        store.Save(_characters);
                    }

                    _saving = false;
                    _saveCts = null;
                    return;
                }
            }
        }
        catch (Exception)
        {
            lock (_gate)
            {
                if (!token.IsCancellationRequested)
                {
                    // The wait itself failed: drop the pending state so the next update starts a new one.
                    _saving = false;
                    _saveCts = null;
                }
            }
        }
    }

    // Saves the live updates not saved yet at once and ends the pending wait.
    private void FlushSave()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _saveCts;
            _saveCts = null;
            _saving = false;
            if (_dirty)
            {
                _dirty = false;
                store.Save(_characters);
            }
        }

        Cancel(cts);
    }

    private bool EndRun(CaptureState state)
    {
        bool changed;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            changed = EndRunLocked(state, out cts);
        }

        Cancel(cts);
        return changed;
    }

    // Detaches the current run (its late captures are ignored) and sets the state. The caller cancels the token outside the lock.
    private bool EndRunLocked(CaptureState state, out CancellationTokenSource? cts)
    {
        cts = null;
        if (_state != CaptureState.Listening)
        {
            return false;
        }

        cts = _current?.Cts;
        _current = null;
        _state = state;
        return true;
    }

    private static void Cancel(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task Settle(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // How the source ended after the cancel does not change the outcome.
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class Run(CaptureEngine engine)
    {
        public CaptureEngine Engine { get; } = engine;

        public CancellationTokenSource Cts { get; } = new();

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
