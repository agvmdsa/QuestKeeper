namespace AqwCompanion.Core.Capture;

/// <summary>Identifies one TCP connection by its endpoints; equality is by value.</summary>
public readonly record struct ConnectionKey(string Source, int SourcePort, string Destination, int DestinationPort)
{
    /// <summary>The key of the connection a segment belongs to, with the addresses as upper-case hex strings.</summary>
    public static ConnectionKey From(TcpSegment segment) =>
        new(
            Convert.ToHexString(segment.SourceAddress),
            segment.SourcePort,
            Convert.ToHexString(segment.DestinationAddress),
            segment.DestinationPort);
}
