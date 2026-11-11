namespace AqwCompanion.App.Phone;

/// <summary>Where the pairing window gets a one-time link from (the phone controller); a seam so the window can be tested without Tailscale.</summary>
public interface IPairingLinkSource
{
    /// <summary>A fresh one-time pairing link, or null when none can be made right now.</summary>
    string? CreatePairingLink();

    /// <summary>The sentence to show instead of a QR code while <see cref="CreatePairingLink"/> returns null.</summary>
    string UnavailableText { get; }
}
