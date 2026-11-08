namespace AqwCompanion.Core.Capture;

/// <summary>
/// What a packet source can say about its own run, to explain why nothing was captured. The text contains only numbers and
/// exception type names, never a device name, an address, packet content or a message text.
/// </summary>
public interface ICaptureDiagnostics
{
    /// <summary>A one-line summary of the counts so far. Never throws; empty when there is nothing to say.</summary>
    string Describe();
}
