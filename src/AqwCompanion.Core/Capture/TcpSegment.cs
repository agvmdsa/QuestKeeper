namespace AqwCompanion.Core.Capture;

/// <summary>One TCP segment of the game connection, copied out of the frame it came in.</summary>
public sealed record TcpSegment(
    byte[] SourceAddress,
    int SourcePort,
    byte[] DestinationAddress,
    int DestinationPort,
    uint Sequence,
    bool IsSyn,
    byte[] Payload);
