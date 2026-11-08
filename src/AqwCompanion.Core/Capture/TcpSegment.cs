namespace AqwCompanion.Core.Capture;

/// <summary>One TCP segment of the game connection, copied out of the frame it came in. <paramref name="IsFin"/> and <paramref name="IsRst"/> say the sender closes the connection.</summary>
public sealed record TcpSegment(
    byte[] SourceAddress,
    int SourcePort,
    byte[] DestinationAddress,
    int DestinationPort,
    uint Sequence,
    bool IsSyn,
    byte[] Payload,
    bool IsFin = false,
    bool IsRst = false);
