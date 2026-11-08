using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A packet source whose run is a delegate; counts its runs.</summary>
public sealed class FakePacketSource(Func<Action<TcpSegment>, CancellationToken, Task> run) : IPacketSource
{
    private int _runs;

    public int Runs => Volatile.Read(ref _runs);

    public Task RunAsync(Action<TcpSegment> onSegment, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runs);
        return run(onSegment, cancellationToken);
    }
}
