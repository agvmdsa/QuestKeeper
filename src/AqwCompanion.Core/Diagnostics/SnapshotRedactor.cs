using System.Text.RegularExpressions;

namespace AqwCompanion.Core.Diagnostics;

public sealed partial class SnapshotRedactor
{
    public const string Mask = "[REDACTED]";

    private readonly Regex[] _termPatterns;

    public SnapshotRedactor(IEnumerable<string>? sensitiveTerms = null)
    {
        _termPatterns = (sensitiveTerms ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new Regex(Regex.Escape(t), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
    }

    public string Redact(string input)
    {
        var output = CookieHeader().Replace(input, "$1 " + Mask);
        output = BearerToken().Replace(output, "$1" + Mask);
        output = SensitiveKeyValue().Replace(output, "$1\"" + Mask + "\"");
        output = Email().Replace(output, Mask);

        foreach (var pattern in _termPatterns)
        {
            output = pattern.Replace(output, Mask);
        }

        return output;
    }

    [GeneratedRegex("""^([ \t]*(?:set-)?cookie[ \t]*:)[^\r\n]*""", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CookieHeader();

    [GeneratedRegex("""(bearer\s+)[A-Za-z0-9._~+/=\-]+""", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(
        """((?:access_?token|refresh_?token|id_?token|token|session_?id|session|csrf|password|passwd|pwd|secret|api_?key)[A-Za-z0-9_\-]*"?\s*[:=]\s*)(?!"?\[REDACTED\])("[^"]*"|'[^']*'|[^\s&;,<>"']+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKeyValue();

    [GeneratedRegex("""[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}""", RegexOptions.IgnoreCase)]
    private static partial Regex Email();
}
