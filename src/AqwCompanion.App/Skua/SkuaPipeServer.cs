using System.IO.Pipes;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Skua;

/// <summary>
/// Receives the lines of the Skua plugin over a named pipe and hands them to the bridge. Read-only by construction:
/// the pipe is created inbound-only and nothing in this class writes to it. One connection at a time.
/// </summary>
public sealed class SkuaPipeServer(string pipeName, SkuaBridge bridge, Action<Exception>? onError = null) : IAsyncDisposable
{
    /// <summary>The only direction the server pipe is ever created with. Delivery 1 has no command path to the plugin.</summary>
    public const PipeDirection Direction = PipeDirection.In;

    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly object _lock = new();
    private int _listeningCount;
    private readonly CancellationTokenSource _cancel = new();
    private NamedPipeServerStream? _current;
    private Task? _loop;
    private bool _disposed;

    /// <summary>Test seam: raised each time a pipe instance has been created and waits for a client.</summary>
    internal event EventHandler? Listening;

    /// <summary>Test seam: how many pipe instances have been created so far.</summary>
    internal int ListeningCount => Volatile.Read(ref _listeningCount);

    public void Start()
    {
        lock (_lock)
        {
            if (_loop is not null || _disposed)
            {
                return;
            }

            _loop = Task.Run(() => RunAsync(_cancel.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            loop = _loop;
        }

        await _cancel.CancelAsync().ConfigureAwait(false);
        lock (_lock)
        {
            // Ends a blocked WaitForConnectionAsync or ReadAsync at once; the loop sees the cancellation and leaves quietly.
            _current?.Dispose();
        }

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(DisposeWait).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Bounded wait: a timeout or a failed loop must not keep the app from closing.
            }
        }

        _cancel.Dispose();
        bridge.Flush();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? lastCreateFailure = null;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                // CurrentUserOnly keeps other users out. UNVERIFIED on Windows: whether an elevated Questkeeper accepts a
                // non-elevated Skua of the same user (same SID, different integrity level); see the owner's Windows checklist.
                pipe = new NamedPipeServerStream(
                    pipeName, Direction, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                lastCreateFailure = null;
                lock (_lock)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    _current = pipe;
                }

                Interlocked.Increment(ref _listeningCount);
                Listening?.Invoke(this, EventArgs.Empty);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await ServeAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ct.IsCancellationRequested)
            {
                _ = ex;
                return;
            }
            catch (Exception ex)
            {
                // A pipe that cannot be created (another instance owns the name) repeats every retry; say it once.
                var key = pipe is null ? ex.GetType().FullName : null;
                if (key is null || key != lastCreateFailure)
                {
                    lastCreateFailure = key;
                    Report(ex);
                }

                try
                {
                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            finally
            {
                lock (_lock)
                {
                    _current = null;
                }

                pipe?.Dispose();
            }
        }
    }

    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        bridge.ClientConnected();
        try
        {
            await foreach (var line in BoundedLineReader.ReadAsync(stream, ct).ConfigureAwait(false))
            {
                var message = line.TooLong || line.Text is null ? null : SkuaLine.Parse(line.Text);
                if (message is null)
                {
                    bridge.Reject();
                }
                else
                {
                    bridge.Accept(message);
                }
            }
        }
        finally
        {
            bridge.ClientDisconnected();
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            onError?.Invoke(ex);
        }
        catch (Exception)
        {
            // The error callback must never stop the server.
        }
    }
}
