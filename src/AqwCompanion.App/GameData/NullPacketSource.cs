using AqwCompanion.Core.Capture;

namespace AqwCompanion.App.GameData;

/// <summary>The packet source on systems without a live capture: delivers nothing and ends at once.</summary>
public sealed class NullPacketSource : IPacketSource
{
    public Task RunAsync(Action<TcpSegment> onSegment, CancellationToken cancellationToken) => Task.CompletedTask;
}
