using Serilog;

namespace AqwCompanion.App.SignIn;

/// <summary>
/// The countdown for the first page load. It never touches the sign-in state itself; only the
/// <c>onTimeout</c> callback does.
/// </summary>
internal sealed class NavigationWatchdog
{
    private readonly TimeSpan _timeout;
    private readonly ILogger _log;
    private readonly Func<bool> _isStopped;
    private readonly Action _onTimeout;
    private CancellationTokenSource? _cts;

    /// <param name="timeout">How long to wait for the first page to finish loading.</param>
    /// <param name="log">Where diagnostics go.</param>
    /// <param name="isStopped">True once the flow is finished or disposed; the timeout is then ignored.</param>
    /// <param name="onTimeout">What to do when nothing loaded in time.</param>
    public NavigationWatchdog(TimeSpan timeout, ILogger log, Func<bool> isStopped, Action onTimeout)
    {
        _timeout = timeout;
        _log = log;
        _isStopped = isStopped;
        _onTimeout = onTimeout;
    }

    public bool PageLoaded { get; private set; }

    /// <summary>The running countdown; null while none is armed. Exposed so tests can await it.</summary>
    public Task? Running { get; private set; }

    public void Start()
    {
        Stop();
        if (PageLoaded)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        Running = WatchAsync(cts.Token);
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        Running = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>Records that a page finished loading and stops the countdown. True only for the first call.</summary>
    public bool MarkPageLoaded()
    {
        if (PageLoaded)
        {
            return false;
        }

        PageLoaded = true;
        Stop();
        return true;
    }

    // Deliberately a real timer rather than IDelay: it must be independent of the "Connected" pause. The
    // continuation resumes on the UI thread because it is started from there.
    private async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_timeout, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (ct.IsCancellationRequested || _isStopped())
            {
                return;
            }

            _log.Warning("No page finished loading within {Timeout}", _timeout);
            _onTimeout();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error("The navigation watchdog failed: {ExceptionType}", ex.GetType().Name);
        }
    }
}
