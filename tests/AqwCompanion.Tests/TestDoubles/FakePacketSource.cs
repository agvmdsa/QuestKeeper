using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A packet source whose run is a delegate; counts its runs and reports <see cref="Text"/> as its diagnostics.</summary>
public sealed class FakePacketSource(Func<Action<TcpSegment>, CancellationToken, Task> run) : IPacketSource, ICaptureDiagnostics
{
    private int _runs;
    private volatile string _text = string.Empty;

    /// <summary>What <see cref="Describe"/> returns; change it at any time.</summary>
    public string Text
    {
        get => _text;
        set => _text = value;
    }

    public string Describe() => _text;

    public int Runs => Volatile.Read(ref _runs);

    public Task RunAsync(Action<TcpSegment> onSegment, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runs);
        return run(onSegment, cancellationToken);
    }
}
