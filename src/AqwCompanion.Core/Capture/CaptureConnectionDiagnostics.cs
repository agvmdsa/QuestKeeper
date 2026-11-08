namespace AqwCompanion.Core.Capture;

/// <summary>
/// The shape of one unreadable message: sizes, the marker reading, a class of its first bytes and its entropy rounded to 0.1.
/// Never a byte, a value or a string of the message.
/// </summary>
public sealed record UnreadableSample(int EncodedLength, string Reading, int DecodedLength, string FirstBytes, double EntropyBits);

/// <summary>
/// A snapshot of how the connections went, as counts only, plus the shapes of the first few unreadable messages.
/// <see cref="ConnectionsWithSyn"/> counts the connections whose SYN-ACK was seen, so the start of their stream is known.
/// </summary>
public sealed record CaptureConnectionDiagnostics(
    int ConnectionsSeen,
    int ConnectionsWithSyn,
    int ConnectionsWithProfile,
    int ConnectionsWithInventory,
    int ConnectionsCompleted,
    IReadOnlyList<UnreadableSample> UnreadableSamples)
{
    /// <summary>The most samples ever kept: the first ones of the session.</summary>
    public const int MaxSamples = 6;

    /// <summary>Everything zero.</summary>
    public static CaptureConnectionDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, []);
}
