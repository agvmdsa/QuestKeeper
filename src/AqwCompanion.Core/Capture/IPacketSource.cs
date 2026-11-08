namespace AqwCompanion.Core.Capture;

/// <summary>Where TCP segments of the game traffic come from: a live network device or a pcap file.</summary>
public interface IPacketSource
{
    /// <summary>
    /// Delivers every TCP segment whose source or destination port is a game port, in arrival order, until the source
    /// ends or cancellation. Returns normally when the source ends (end of file) or the token is cancelled.
    /// Never throws for malformed input.
    /// </summary>
    Task RunAsync(Action<TcpSegment> onSegment, CancellationToken cancellationToken);
}
