using System.Text.Json;
using System.Text.RegularExpressions;

namespace AqwCompanion.App.Phone;

/// <summary>Reads <c>tailscale status --json</c>. Everything in it is outside input: missing or odd fields give Error, never an exception.</summary>
public static class TailscaleStatusParser
{
    public const int MaxJsonLength = 256 * 1024;

    private const int MaxDnsNameLength = 253;

    private static readonly Regex DnsNamePattern = new(@"^[a-z0-9-]+(\.[a-z0-9-]+)+\z", RegexOptions.CultureInvariant | RegexOptions.ECMAScript);

    private static readonly TailscaleState Failed = new(TailscaleStateKind.Error, null, TailscaleService.ErrorText);

    public static TailscaleState Parse(string statusJson)
    {
        if (string.IsNullOrWhiteSpace(statusJson) || statusJson.Length > MaxJsonLength)
        {
            return Failed;
        }

        try
        {
            using var doc = JsonDocument.Parse(statusJson, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("BackendState", out var backend) || backend.ValueKind != JsonValueKind.String)
            {
                return Failed;
            }

            switch (backend.GetString())
            {
                case "NeedsLogin" or "NeedsMachineAuth" or "Stopped":
                    return new TailscaleState(TailscaleStateKind.NotSignedIn, null, TailscaleService.NotSignedInText);
                case "Running":
                    return ParseRunning(root);
                default:
                    return Failed;
            }
        }
        catch (Exception)
        {
            return Failed;
        }
    }

    /// <summary>Lower-cases and strips one trailing dot, then accepts only dotted letters, digits and hyphens, at most 253 characters.</summary>
    public static bool IsValidDnsName(string name) => Normalize(name) is not null;

    private static TailscaleState ParseRunning(JsonElement root)
    {
        var certs = false;
        if (root.TryGetProperty("CertDomains", out var certDomains))
        {
            if (certDomains.ValueKind == JsonValueKind.Array)
            {
                certs = certDomains.GetArrayLength() > 0;
            }
            else if (certDomains.ValueKind != JsonValueKind.Null)
            {
                return Failed;
            }
        }

        var magicDnsOff = root.TryGetProperty("CurrentTailnet", out var tailnet)
            && tailnet.ValueKind == JsonValueKind.Object
            && tailnet.TryGetProperty("MagicDNSEnabled", out var magic)
            && magic.ValueKind == JsonValueKind.False;
        if (!certs || magicDnsOff)
        {
            return new TailscaleState(TailscaleStateKind.HttpsNotEnabled, null, TailscaleService.HttpsNotEnabledText);
        }

        if (!root.TryGetProperty("Self", out var self) || self.ValueKind != JsonValueKind.Object
            || !self.TryGetProperty("DNSName", out var dns) || dns.ValueKind != JsonValueKind.String
            || Normalize(dns.GetString()) is not { } name)
        {
            return Failed;
        }

        return new TailscaleState(TailscaleStateKind.Ready, name, TailscaleService.ReadyText);
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Any(c => c > 127))
        {
            return null;
        }

        var name = raw.ToLowerInvariant();
        if (name.EndsWith('.'))
        {
            name = name[..^1];
        }

        return name.Length is > 0 and <= MaxDnsNameLength && DnsNamePattern.IsMatch(name) ? name : null;
    }
}
