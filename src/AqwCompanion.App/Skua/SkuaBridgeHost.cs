using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Skua;

/// <summary>
/// Owns the pipe server for the composition root. Implements IDisposable (not only IAsyncDisposable) because
/// ServiceProvider.Dispose() disposes singletons that offer either, and the app exit calls the synchronous one.
/// </summary>
public sealed class SkuaBridgeHost : IDisposable
{
    private readonly SkuaPipeServer _server;
    private readonly object _lock = new();
    private bool _started;
    private bool _disposed;

    public SkuaBridgeHost(SkuaBridge bridge, Action<Exception>? onError = null, string pipeName = SkuaProtocol.PipeName)
    {
        _server = new SkuaPipeServer(pipeName, bridge, onError);
    }

    /// <summary>Starts listening; safe to call again. A pipe that cannot be created is reported through the error callback.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        _server.Start();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        // The server bounds its own wait and never captures a context, so waiting here cannot deadlock the UI thread.
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
