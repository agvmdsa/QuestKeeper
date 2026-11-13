using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Skua;

/// <summary>
/// The two-way named pipe to the Skua plugins: up to <see cref="SkuaProtocol.MaxInstances"/> connections at once, each with
/// its own reader (lines to the hub) and its own queue of outgoing lines (commands from the hub), plus one pipe that always
/// waits for the next plugin. A ninth plugin is closed at once, quietly. A plugin that never reads (one that fell back to a
/// send-only pipe) only fills its own bounded queue; nothing waits on it.
/// </summary>
public sealed class SkuaPipeServer(string pipeName, SkuaHub hub, Action<Exception>? onError = null) : IAsyncDisposable
{
    /// <summary>The direction the server pipe is created with: lines from the plugin, commands to it.</summary>
    public const PipeDirection Direction = PipeDirection.InOut;

    /// <summary>Pipe instances of the name at most: every allowed connection plus the waiting spare that refuses a ninth plugin.</summary>
    public const int MaxPipes = SkuaProtocol.MaxInstances + 1;

    /// <summary>Lines waiting for one plugin at most; a send beyond fails at once (the hub then reports no answer).</summary>
    public const int QueueLength = 32;

    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly object _lock = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly HashSet<Connection> _connections = [];
    private int _listeningCount;
    private NamedPipeServerStream? _listening;
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

            hub.Forgotten += OnForgotten;
            _loop = Task.Run(() => RunAsync(_cancel.Token));
        }
    }

    /// <summary>
    /// After a Forget the hub has no instances, and a plugin re-sends its status only when it changes. Closing every
    /// connection makes each plugin reconnect and send its hello and last status again: it notices at its next write, at the
    /// latest its 10 s keep-alive, and retries 1 s later. The pipe that is still waiting for a client is left alone.
    /// </summary>
    private void OnForgotten(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            foreach (var connection in _connections)
            {
                connection.Close(dropped: true);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Task> running;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            running = _loop is null ? [] : [_loop];
        }

        hub.Forgotten -= OnForgotten;
        await _cancel.CancelAsync().ConfigureAwait(false);
        lock (_lock)
        {
            // Ends a blocked WaitForConnectionAsync, ReadAsync or WriteAsync at once; the loops see the cancellation and leave quietly.
            _listening?.Dispose();
            foreach (var connection in _connections)
            {
                connection.Close(dropped: false);
                if (connection.Serving is { } serving)
                {
                    running.Add(serving);
                }
            }
        }

        try
        {
            await Task.WhenAll(running).WaitAsync(DisposeWait).ConfigureAwait(false);
            _cancel.Dispose();
        }
        catch (Exception)
        {
            // Bounded wait: a timeout or a failed loop must not keep the app from closing.
        }

        hub.Flush();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? lastCreateFailure = null;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            var created = false;
            try
            {
                pipe = CreatePipe(pipeName);
                created = true;
                lastCreateFailure = null;
                lock (_lock)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    _listening = pipe;
                }

                Interlocked.Increment(ref _listeningCount);
                Listening?.Invoke(this, EventArgs.Empty);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                lock (_lock)
                {
                    _listening = null;
                }

                if (TryServe(pipe, ct))
                {
                    pipe = null;   // the connection owns it now
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A pipe that cannot be created (another instance owns the name) repeats every retry; say it once.
                var key = created ? null : ex.GetType().FullName;
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
                    if (ReferenceEquals(_listening, pipe))
                    {
                        _listening = null;
                    }
                }

                // A refused ninth plugin, or a pipe that failed: closed here. A served pipe was handed over (pipe is null).
                pipe?.Dispose();
            }
        }
    }

    /// <summary>Hands a connected pipe to its own connection; false when all slots are taken (the caller closes it quietly).</summary>
    private bool TryServe(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var connection = new Connection(pipe);
        lock (_lock)
        {
            if (_connections.Count >= SkuaProtocol.MaxInstances || hub.Attach(connection) is not { } attachment)
            {
                return false;
            }

            _connections.Add(connection);
            connection.Serving = Task.Run(() => ServeAsync(connection, attachment, ct));
            return true;
        }
    }

    /// <summary>
    /// Windows: an explicit security descriptor (see <see cref="CreateUserOnlySecurity"/>) instead of
    /// <see cref="PipeOptions.CurrentUserOnly"/>, because .NET builds that one from the token's default owner, which for an
    /// elevated administrator (SkuaLink always runs elevated for capture) is BUILTIN\Administrators: a normal Skua holds
    /// that group deny-only and would be refused, and the plugin's own CurrentUserOnly owner check would fail.
    /// Elsewhere (tests on macOS/Linux): CurrentUserOnly as before.
    /// UNVERIFIED on Windows: a real elevated SkuaLink with a non-elevated Skua (the owner's checklist), including
    /// whether the pipe's default integrity label lets a medium-integrity client read from and write to it.
    /// </summary>
    private static NamedPipeServerStream CreatePipe(string name) =>
        OperatingSystem.IsWindows()
            ? NamedPipeServerStreamAcl.Create(
                name, Direction, MaxPipes, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, CreateUserOnlySecurity())
            : new NamedPipeServerStream(name, Direction, MaxPipes, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>
    /// Owner and only grantee are the user's own SID (not the Administrators group an elevated token uses as owner), and
    /// inherited entries are blocked, so no other account, other users included, has any access. The right is FullControl
    /// for that one SID, exactly what .NET's CurrentUserOnly grants, only to the user instead of the token owner: the client
    /// opens with GENERIC_READ | GENERIC_WRITE (or GENERIC_WRITE alone in its send-only fallback), which for a pipe includes
    /// FILE_APPEND_DATA (the same bit as CreateNewInstance) and READ_CONTROL (needed by the client's owner check), and the
    /// server needs CreateNewInstance to open the next instance. A narrower set is not provable without Windows.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static PipeSecurity CreateUserOnlySecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current Windows identity has no user SID.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private async Task ServeAsync(Connection connection, IDisposable attachment, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writer = connection.WriteAllAsync(stop.Token);
        try
        {
            await foreach (var line in BoundedLineReader.ReadAsync(connection.Pipe, stop.Token, SkuaProtocol.MaxResultLineBytes).ConfigureAwait(false))
            {
                var message = line.TooLong || line.Text is null ? null : SkuaLine.Parse(line.Text);
                if (message is null)
                {
                    hub.Reject(attachment);
                }
                else
                {
                    hub.Accept(attachment, message);
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested || connection.Dropped)
        {
            // Closed by Dispose, or on purpose after a Forget (see OnForgotten): not an error.
        }
        catch (IOException)
        {
            // The plugin went away mid-line: an ordinary disconnect.
        }
        catch (Exception ex)
        {
            Report(ex);
        }
        finally
        {
            attachment.Dispose();
            connection.Close(dropped: false);
            await stop.CancelAsync().ConfigureAwait(false);
            await writer.ConfigureAwait(false);
            lock (_lock)
            {
                _connections.Remove(connection);
            }
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

    /// <summary>One connected plugin: its pipe and its queue of outgoing lines.</summary>
    private sealed class Connection(NamedPipeServerStream pipe) : ISkuaConnection
    {
        private readonly Channel<string> _outgoing = Channel.CreateBounded<string>(
            new BoundedChannelOptions(QueueLength) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

        private volatile bool _dropped;

        public NamedPipeServerStream Pipe { get; } = pipe;

        public Task? Serving { get; set; }

        public bool Dropped => _dropped;

        /// <summary>
        /// Queues the line. A command goes through <see cref="SkuaLine.ToCommandLine"/>; one over the command cap is refused
        /// here and never written (the hub already answers such a command itself).
        /// </summary>
        public Task SendAsync(SkuaMessage message, CancellationToken ct)
        {
            var line = message is SkuaCommand command ? SkuaLine.ToCommandLine(command) : SkuaLine.ToLine(message);
            if (line is null)
            {
                return Task.FromException(new ArgumentException("The command is over the command line cap.", nameof(message)));
            }

            return _outgoing.Writer.TryWrite(line)
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException("The Skua pipe is closed or its queue is full."));
        }

        public async Task WriteAllAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var line in _outgoing.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await Pipe.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), ct).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // A closed or broken pipe ends the writing; the reader sees the disconnect.
            }
        }

        public void Close(bool dropped)
        {
            if (dropped)
            {
                _dropped = true;
            }

            _outgoing.Writer.TryComplete();
            try
            {
                Pipe.Dispose();
            }
            catch (Exception)
            {
                // Closing a broken pipe can fail; it is gone either way.
            }
        }
    }
}
