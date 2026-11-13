using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>The write side of one plugin pipe, as the pipe server offers it to the hub.</summary>
public interface ISkuaConnection
{
    /// <summary>Queues the message for the plugin. Fails (never blocks for long) when the pipe is closed or its queue is full.</summary>
    Task SendAsync(SkuaMessage message, CancellationToken ct);
}

/// <summary>Sends a command to one Skua and waits for its answer.</summary>
public interface ISkuaCommandChannel
{
    /// <summary>
    /// The plugin's answer, or null when it did not answer in time (or the pipe closed or failed meanwhile). Throws
    /// <see cref="KeyNotFoundException"/> for an unknown instance and <see cref="InvalidOperationException"/> for a disconnected one.
    /// </summary>
    Task<ResultMessage?> SendCommandAsync(string instanceId, SkuaCommand command, CancellationToken ct);
}
