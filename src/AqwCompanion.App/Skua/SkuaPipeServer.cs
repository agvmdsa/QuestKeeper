using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
    private NamedPipeServerStream? _serving;
    private NamedPipeServerStream? _dropped;
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

            bridge.Forgotten += OnForgotten;
            _loop = Task.Run(() => RunAsync(_cancel.Token));
        }
    }

    /// <summary>
    /// After a Forget the bridge has no status, and the plugin re-sends one only when it changes. Closing the connection
    /// (never writing to it) makes the plugin reconnect and send its hello and last status again: it notices at its next write,
    /// at the latest its 10 s keep-alive, and retries 1 s later. A pipe that is still waiting for a client is left alone.
    /// </summary>
    private void OnForgotten(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_serving is { } pipe)
            {
                _dropped = pipe;
                pipe.Dispose();
            }
        }
    }

    private bool WasDropped(NamedPipeServerStream? pipe)
    {
        lock (_lock)
        {
            return pipe is not null && ReferenceEquals(_dropped, pipe);
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

        bridge.Forgotten -= OnForgotten;
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
                pipe = CreatePipe(pipeName);
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
                lock (_lock)
                {
                    _serving = pipe;
                }

                await ServeAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception) when (WasDropped(pipe))
            {
                // Closed on purpose after a Forget (see OnForgotten): not an error, listen again at once.
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
                    _serving = null;
                    _dropped = null;
                }

                pipe?.Dispose();
            }
        }
    }

    /// <summary>
    /// Windows: an explicit security descriptor (see <see cref="CreateUserOnlySecurity"/>) instead of
    /// <see cref="PipeOptions.CurrentUserOnly"/>, because .NET builds that one from the token's default owner, which for an
    /// elevated administrator (SkuaLink always runs elevated for capture) is BUILTIN\Administrators: a normal Skua holds
    /// that group deny-only and would be refused, and the plugin's own CurrentUserOnly owner check would fail.
    /// Elsewhere (tests on macOS/Linux): CurrentUserOnly as before.
    /// UNVERIFIED on Windows: a real elevated SkuaLink with a non-elevated Skua (the owner's checklist), including
    /// whether the pipe's default integrity label lets a medium-integrity client write to it.
    /// </summary>
    private static NamedPipeServerStream CreatePipe(string name) =>
        OperatingSystem.IsWindows()
            ? NamedPipeServerStreamAcl.Create(
                name, Direction, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, CreateUserOnlySecurity())
            : new NamedPipeServerStream(name, Direction, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>
    /// Owner and only grantee are the user's own SID (not the Administrators group an elevated token uses as owner), and
    /// inherited entries are blocked, so no other account, other users included, has any access. The right is FullControl
    /// for that one SID, exactly what .NET's CurrentUserOnly grants, only to the user instead of the token owner: the client
    /// opens with GENERIC_WRITE, which for a pipe maps to FILE_GENERIC_WRITE and includes FILE_APPEND_DATA (the same bit as
    /// CreateNewInstance) and READ_CONTROL (needed by the client's owner check), and the server needs CreateNewInstance to
    /// open the next instance. A narrower set is not provable without Windows. Data still flows one way: the server is
    /// created <see cref="Direction"/> and never writes.
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
