namespace AqwCompanion.Core.Capture;

/// <summary>
/// A snapshot of the live reading after the logins, as numbers only. The last gold and exp amounts are the Character's own gains
/// from the last applied <c>addGoldExp</c> event; they are numbers the player compares with the game, never a name or a text.
/// </summary>
public sealed record CaptureLiveDiagnostics(
    int LiveConnections,
    int LiveEventsApplied,
    int LiveEventsIgnored,
    int LiveEventsUnmatched,
    int LiveEncodedSkipped,
    long LastGoldGain,
    long LastExpGain)
{
    /// <summary>Everything zero.</summary>
    public static CaptureLiveDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}
