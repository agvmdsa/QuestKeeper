namespace AqwCompanion.App.Phone;

/// <summary>A pairing link and the one-time token inside it; the token is kept only so the link can be withdrawn. Never log or print it.</summary>
public sealed record PairingOffer(string Link, string Token)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Link = <redacted>, Token = <redacted>");
        return true;
    }
}

/// <summary>Where the pairing window gets a one-time link from (the phone controller); a seam so the window can be tested without Tailscale.</summary>
public interface IPairingLinkSource
{
    /// <summary>A fresh one-time pairing link with its token, or null when none can be made right now.</summary>
    PairingOffer? CreateOffer();

    /// <summary>Whether a link made now would work (the host runs and the page is published through Tailscale).</summary>
    bool IsAvailable { get; }

    /// <summary>The sentence to show instead of a QR code while no link can be made.</summary>
    string UnavailableText { get; }
}
