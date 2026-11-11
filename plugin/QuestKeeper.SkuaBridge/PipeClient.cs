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
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan FirstRetryWait = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    /// <summary>An empty line is skipped by Questkeeper's line reader; writing one now and then finds a closed Questkeeper while nothing happens.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

    private static readonly byte[] NewLine = "\n"u8.ToArray();

    // A null entry means "send the latest status here". Status itself is never queued, so a reconnect cannot replay an old one.
    private readonly Channel<string?> _queue = Channel.CreateBounded<string?>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });

    private readonly CancellationTokenSource _cancel = new();
    private readonly object _lock = new();
    private readonly string _helloLine;
    private string? _statusLine;
    private Task? _loop;
    private bool _disposed;

    public PipeClient(SkuaHello hello)
    {
        _helloLine = SkuaLine.ToLine(new HelloMessage(hello));
    }

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
                _queue.Writer.TryWrite(null);
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
            if (loop is null || loop.Wait(DisposeWait))
            {
                _cancel.Dispose();
            }
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
                await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, ct).ConfigureAwait(false);
                wait = FirstRetryWait;

                await WriteAsync(pipe, _helloLine, ct).ConfigureAwait(false);
                if (Volatile.Read(ref _statusLine) is { } status)
                {
                    await WriteAsync(pipe, status, ct).ConfigureAwait(false);
                }

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
