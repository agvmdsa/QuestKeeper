using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using AqwCompanion.Skua;

namespace QuestKeeper.SkuaBridge;

/// <summary>
/// Sends protocol lines to Questkeeper over the local named pipe. Outbound only: the pipe is opened
/// <see cref="PipeDirection.Out"/>, so nothing can ever be read from Questkeeper. Retries for the whole life of the plugin
/// (Questkeeper may start after Skua, close, or restart). No public member ever throws.
/// </summary>
internal sealed class PipeClient : IDisposable
{
    public const int Capacity = 256;

    /// <summary>
    /// One connect attempt, no timeout loop. On Windows, NamedPipeClientStream.Connect(timeout) spins a thread until the
    /// timeout while the pipe does not exist (WaitNamedPipe fails at once and .NET retries); with 0 it tries once and, when the
    /// pipe exists but its instance is busy, waits only the server's default (50 ms). The retry waits below set the pace.
    /// </summary>
    public const int ConnectTimeoutMs = 0;
    public static readonly TimeSpan FirstRetryWait = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    /// <summary>An empty line is skipped by Questkeeper's line reader; writing one now and then finds a closed Questkeeper while nothing happens.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

    private static readonly byte[] NewLine = "\n"u8.ToArray();

    // A null entry means "send the latest status here". Status itself is never queued, so a reconnect cannot replay an old one,
    // and at most one marker is pending (_statusQueued), so frequent status changes during a long disconnect cannot push log
    // lines out of the bounded queue or queue repeated writes of the same status.
    // A dropped marker clears the flag so the next change queues one again; a reconnect writes the latest status anyway.
    private readonly Channel<string?> _queue;

    private readonly CancellationTokenSource _cancel = new();
    private readonly object _lock = new();
    private readonly string _helloLine;
    private readonly Action? _onConnected;
    private string? _statusLine;
    private int _statusQueued;
    private bool _connected;
    private Task? _loop;
    private bool _disposed;

    public PipeClient(SkuaHello hello, Action? onConnected = null)
    {
        _queue = Channel.CreateBounded<string?>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false },
            dropped =>
            {
                if (dropped is null)
                {
                    Volatile.Write(ref _statusQueued, 0);
                }
            });
        _helloLine = SkuaLine.ToLine(new HelloMessage(hello));
        _onConnected = onConnected;
    }

    /// <summary>True while a pipe to Questkeeper is open (hello written and no write has failed since).</summary>
    public bool IsConnected => Volatile.Read(ref _connected);

    public void Start()
    {
        try
        {
            lock (_lock)
            {
                if (_loop is null && !_disposed)
                {
                    _loop = Task.Run(() => RunAsync(_cancel.Token));
                }
            }
        }
        catch (Exception)
        {
            // Never throw into Skua; without a loop the plugin simply reports nothing.
        }
    }

    /// <summary>Queues a message without blocking. When the queue is full the oldest entry is dropped.</summary>
    public void Send(SkuaMessage message)
    {
        try
        {
            if (Volatile.Read(ref _disposed))
            {
                return;
            }

            var line = SkuaLine.ToLine(message);
            if (message is StatusMessage)
            {
                Volatile.Write(ref _statusLine, line);
                if (Interlocked.Exchange(ref _statusQueued, 1) == 0 && !_queue.Writer.TryWrite(null))
                {
                    Volatile.Write(ref _statusQueued, 0);
                }
            }
            else
            {
                _queue.Writer.TryWrite(line);
            }
        }
        catch (Exception)
        {
            // A message that cannot be built or queued is dropped; Skua must not notice.
        }
    }

    public void Dispose()
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

        try
        {
            _queue.Writer.TryComplete();
            _cancel.Cancel();
            loop?.Wait(DisposeWait);
        }
        catch (Exception)
        {
            // Bounded and quiet: unloading the plugin must not fail or hang Skua.
        }

        try
        {
            // After the wait. A loop still running only reads IsCancellationRequested, which stays valid after Dispose,
            // and treats any other failure as cancellation.
            _cancel.Dispose();
        }
        catch (Exception)
        {
            // Bounded and quiet: unloading the plugin must not fail or hang Skua.
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var wait = FirstRetryWait;
        string? unsent = null;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(
                    ".", SkuaProtocol.PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);
                wait = FirstRetryWait;

                await WriteAsync(pipe, _helloLine, ct).ConfigureAwait(false);
                if (Volatile.Read(ref _statusLine) is { } status)
                {
                    await WriteAsync(pipe, status, ct).ConfigureAwait(false);
                }

                Volatile.Write(ref _connected, true);
                Notify();

                if (unsent is not null)
                {
                    await WriteAsync(pipe, unsent, ct).ConfigureAwait(false);
                    unsent = null;
                }

                await DrainAsync(pipe, line => unsent = line, ct).ConfigureAwait(false);
                return; // The queue was completed: the client is being disposed.
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Questkeeper is not there, closed the pipe, or the write failed: drop the connection and try again.
            }
            finally
            {
                Volatile.Write(ref _connected, false);
                try
                {
                    pipe?.Dispose();
                }
                catch (Exception)
                {
                    // Disposing a broken pipe may itself fail; nothing to do.
                }
            }

            try
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, MaxRetryWait.Ticks));
        }
    }

    private void Notify()
    {
        try
        {
            _onConnected?.Invoke();
        }
        catch (Exception)
        {
            // The callback only queues work; a failure there must not drop the connection.
        }
    }

    private async Task DrainAsync(Stream pipe, Action<string?> remember, CancellationToken ct)
    {
        var reader = _queue.Reader;
        while (true)
        {
            bool more;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                idle.CancelAfter(KeepAliveInterval);
                try
                {
                    more = await reader.WaitToReadAsync(idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await pipe.WriteAsync(NewLine, ct).ConfigureAwait(false);
                    await pipe.FlushAsync(ct).ConfigureAwait(false);
                    continue;
                }
            }

            if (!more)
            {
                return;
            }

            while (reader.TryRead(out var entry))
            {
                if (entry is null)
                {
                    // Cleared before the status is read: a change from here on queues a new marker, so none is missed.
                    Volatile.Write(ref _statusQueued, 0);
                }

                var line = entry ?? Volatile.Read(ref _statusLine);
                if (line is null)
                {
                    continue;
                }

                // A log line whose write fails is written again after the reconnect; a status is re-sent anyway.
                remember(entry);
                await WriteAsync(pipe, line, ct).ConfigureAwait(false);
                remember(null);
            }
        }
    }

    private static async Task WriteAsync(Stream pipe, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await pipe.WriteAsync(bytes, ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);
    }
}
