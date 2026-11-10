namespace AqwCompanion.App.Phone;

/// <summary>The link a phone opens to pair: the tailnet name over HTTPS, with the one-time token in the fragment (never sent to a server).</summary>
public static class PairingUrl
{
    /// <summary>Throws <see cref="ArgumentException"/> when the name fails the strict name check or the token is empty or not URL-safe; never returns null.</summary>
    public static string Build(string dnsName, string token)
    {
        if (dnsName is null || dnsName.EndsWith('.') || !TailscaleStatusParser.IsValidDnsName(dnsName))
        {
            throw new ArgumentException("Not a valid tailnet name.", nameof(dnsName));
        }

        if (string.IsNullOrEmpty(token) || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("Not a valid pairing token.", nameof(token));
        }

        return $"https://{dnsName}/#t={token}";
    }
}
