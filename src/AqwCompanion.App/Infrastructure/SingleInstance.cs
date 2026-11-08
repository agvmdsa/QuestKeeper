namespace AqwCompanion.App.Infrastructure;

/// <summary>
/// Lets one process own the app: a named mutex says who is first, a named event lets a second launch wake the first.
/// Both names live in the "Local\" namespace (this user session). Where named mutexes or events are not supported the guard
/// degrades instead of throwing: without a mutex every process counts as primary, without an event a signal does nothing.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(2);

    private readonly string _mutexName;
    private readonly bool _supported;
    private readonly EventWaitHandle? _signal;
    private readonly ManualResetEventSlim _release = new(false);
    private readonly ManualResetEvent _stopListening = new(false);
    private readonly object _gate = new();
    private Thread? _owner;
    private Thread? _listener;
    private bool _primary;
    private int _disposed;

    /// <param name="name">Names the mutex and the event: "Local\" plus this name.</param>
    /// <param name="supported">False turns the guard off: every process is primary and signals do nothing.</param>
    public SingleInstance(string name, bool supported = true)
    {
        _mutexName = "Local\\" + name;
        _supported = supported;
        if (!supported)
        {
            return;
        }

        try
        {
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, _mutexName + ".signal");
        }
        catch (Exception)
        {
            // Named events are not available here (for example on Unix): a second launch simply cannot wake the first.
            _signal = null;
        }
    }

    /// <summary>True when this process now owns the app. Waits up to <paramref name="wait"/> for the previous owner to exit.</summary>
    public bool TryBecomePrimary(TimeSpan wait)
    {
        lock (_gate)
        {
            if (_primary)
            {
                return true;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (!_supported)
            {
                _primary = true;
                return true;
            }

            // The mutex belongs to the thread that took it, so a dedicated thread takes it, holds it and releases it.
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _owner = new Thread(() => HoldMutex(wait, result)) { IsBackground = true, Name = "SingleInstance owner" };
            _owner.Start();
            _primary = result.Task.GetAwaiter().GetResult();
            return _primary;
        }
    }

    /// <summary>Wakes the primary instance; does nothing when signals are not available.</summary>
    public void SignalPrimary()
    {
        try
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _signal?.Set();
            }
        }
        catch (Exception)
        {
            // A missed wake-up must not stop the second launch from ending.
        }
    }

    /// <summary>Calls <paramref name="onSignal"/> from a background thread each time a second launch signals; stops on Dispose.</summary>
    public void ListenForSignals(Action onSignal)
    {
        lock (_gate)
        {
            if (_signal is null || _listener is not null || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _listener = new Thread(() => Listen(onSignal)) { IsBackground = true, Name = "SingleInstance listener" };
            _listener.Start();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Thread? listener;
        Thread? owner;
        lock (_gate)
        {
            listener = _listener;
            owner = _owner;
        }

        _stopListening.Set();
        _release.Set();
        if (listener is not null && listener != Thread.CurrentThread)
        {
            listener.Join(JoinTimeout);
        }

        if (owner is not null && owner != Thread.CurrentThread)
        {
            owner.Join(JoinTimeout);
        }

        _signal?.Dispose();
        _stopListening.Dispose();
        _release.Dispose();
    }

    private void HoldMutex(TimeSpan wait, TaskCompletionSource<bool> result)
    {
        Mutex? mutex = null;
        var owned = false;
        try
        {
            mutex = new Mutex(false, _mutexName);
            try
            {
                owned = mutex.WaitOne(wait);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner ended without releasing it; the mutex is ours now.
                owned = true;
            }
        }
        catch (Exception)
        {
            // Named mutexes are not available here: act as the only instance.
            mutex?.Dispose();
            mutex = null;
            owned = true;
        }

        result.TrySetResult(owned);
        if (owned)
        {
            _release.Wait();
        }

        if (mutex is not null)
        {
            if (owned)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (Exception)
                {
                    // Nothing else to do while ending.
                }
            }

            mutex.Dispose();
        }
    }

    private void Listen(Action onSignal)
    {
        WaitHandle[] handles = [_stopListening, _signal!];
        while (WaitHandle.WaitAny(handles) == 1)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                onSignal();
            }
            catch (Exception)
            {
                // A failing callback must not end the listener.
            }
        }
    }
}
