using System.Text;
using System.Text.RegularExpressions;

namespace AqwCompanion.Core.Diagnostics;

/// <summary>
/// Masks credentials and personal data in text destined for diagnostics. Every pass is linear in the input
/// size: regexes are either anchored to rare literal prefixes or guarded by lookbehinds, and the keyed-value
/// scan advances monotonically (each value is consumed once, failures consume to end of line or end of input).
/// </summary>
public sealed partial class SnapshotRedactor
{
    public const string Mask = "[REDACTED]";

    /// <summary>Mask for the second-line rules (addresses and long encoded runs), the same text the log guard writes.</summary>
    public const string NetworkMask = "***";

    private const string EscapedQuote = "\\\"";

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
        output = AuthorizationHeader().Replace(output, "$1" + Mask);
        output = BearerToken().Replace(output, "$1" + Mask);
        output = HtmlTag().Replace(output, RedactHtmlTag);
        output = RedactKeyedValues(output);
        output = Email().Replace(output, Mask);
        output = IPv4().Replace(output, NetworkMask);
        output = IPv6().Replace(output, NetworkMask);
        output = LongBase64Run().Replace(output, NetworkMask);

        foreach (var pattern in _termPatterns)
        {
            output = pattern.Replace(output, Mask);
        }

        return output;
    }

    private static string RedactHtmlTag(Match tagMatch)
    {
        var tag = tagMatch.Value;
        var sensitive = false;

        foreach (Match attribute in NameAttribute().Matches(tag))
        {
            if (SensitiveWord().IsMatch(attribute.Groups["v"].Value))
            {
                sensitive = true;
                break;
            }
        }

        return sensitive ? ValueAttribute().Replace(tag, "$1\"" + Mask + "\"") : tag;
    }

    /// <summary>
    /// Finds sensitive keys (JSON, escaped JSON, single-quoted, query or assignment form) and masks the value
    /// that follows: quoted strings (with escapes), balanced objects/arrays, or bare tokens.
    /// </summary>
    private static string RedactKeyedValues(string text)
    {
        StringBuilder? builder = null;
        var copied = 0;
        var position = 0;

        while (position < text.Length)
        {
            var key = SensitiveKey().Match(text, position);
            if (!key.Success)
            {
                break;
            }

            var valueStart = key.Index + key.Length;
            position = valueStart;
            if (valueStart >= text.Length)
            {
                break;
            }

            var keyName = key.Groups["key"].Value;
            var keyQuoted = key.Groups["quote"].Success;

            // "Authorization: <scheme> <credentials>" is owned by the header rule, which keeps the scheme visible.
            if (!keyQuoted && keyName.StartsWith("authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var escapedContext = keyQuoted && key.Groups["quote"].Value[0] == '\\';
            var cookieToEndOfLine = !keyQuoted
                && keyName.StartsWith("cookie", StringComparison.OrdinalIgnoreCase)
                && key.Groups["sep"].Value == ":";

            if (!TryScanValue(text, valueStart, escapedContext, cookieToEndOfLine, out var valueEnd, out var quote, out var alreadyMasked))
            {
                continue;
            }

            position = valueEnd;
            if (alreadyMasked)
            {
                continue;
            }

            builder ??= new StringBuilder(text.Length);
            builder.Append(text, copied, valueStart - copied).Append(quote).Append(Mask).Append(quote);
            copied = valueEnd;
        }

        if (builder is null)
        {
            return text;
        }

        builder.Append(text, copied, text.Length - copied);
        return builder.ToString();
    }

    private static bool TryScanValue(
        string text, int start, bool escapedContext, bool toEndOfLine, out int end, out string quote, out bool alreadyMasked)
    {
        var bareQuote = escapedContext ? EscapedQuote : "\"";
        alreadyMasked = false;
        quote = bareQuote;
        end = start;

        var c = text[start];

        if (c == '\\' && start + 1 < text.Length && text[start + 1] == '"')
        {
            var close = text.IndexOf(EscapedQuote, start + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                end = EndOfLine(text, start);
            }
            else
            {
                end = close + 2;
                alreadyMasked = text.AsSpan(start + 2, close - start - 2).SequenceEqual(Mask);
            }

            quote = EscapedQuote;
            return true;
        }

        if (c is '"' or '\'')
        {
            var close = FindClosingQuote(text, start + 1, c);
            if (close < 0)
            {
                end = EndOfLine(text, start);
            }
            else
            {
                end = close + 1;
                alreadyMasked = text.AsSpan(start + 1, close - start - 1).SequenceEqual(Mask);
            }

            quote = c.ToString();
            return true;
        }

        if (c is '{' or '[')
        {
            end = FindEndOfStructure(text, start);
            return true;
        }

        var index = start;
        while (index < text.Length && !IsBareTerminator(text[index], toEndOfLine))
        {
            index++;
        }

        if (index == start)
        {
            return false;
        }

        end = index;
        alreadyMasked = text.AsSpan(start, index - start).SequenceEqual(Mask);
        return true;
    }

    private static bool IsBareTerminator(char c, bool toEndOfLine) =>
        toEndOfLine
            ? c is '\r' or '\n'
            : char.IsWhiteSpace(c) || c is '&' or ';' or ',' or '<' or '>' or '"' or '\'' or '}' or ']';

    private static int EndOfLine(string text, int from)
    {
        var index = text.AsSpan(from).IndexOfAny('\r', '\n');
        return index < 0 ? text.Length : from + index;
    }

    /// <summary>Returns the index of the unescaped closing quote, or -1 when the string is unterminated.</summary>
    private static int FindClosingQuote(string text, int from, char quote)
    {
        var index = from;
        while (index < text.Length)
        {
            var found = text.AsSpan(index).IndexOfAny(quote, '\\');
            if (found < 0)
            {
                return -1;
            }

            index += found;
            if (text[index] == quote)
            {
                return index;
            }

            index += 2;
        }

        return -1;
    }

    /// <summary>
    /// Returns the exclusive end of the balanced {...} or [...] starting at <paramref name="start"/>. Strings
    /// (plain or backslash-escaped quotes) are skipped. An unbalanced structure is masked to the end of input,
    /// which keeps the scan linear and errs on the side of privacy.
    /// </summary>
    private static int FindEndOfStructure(string text, int start)
    {
        var depth = 0;
        var index = start;

        while (index < text.Length)
        {
            var c = text[index];

            if (c is '{' or '[')
            {
                depth++;
            }
            else if (c is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index + 1;
                }
            }
            else if (c == '"')
            {
                var close = FindClosingQuote(text, index + 1, '"');
                if (close < 0)
                {
                    return text.Length;
                }

                index = close;
            }
            else if (c == '\\' && index + 1 < text.Length && text[index + 1] == '"')
            {
                var close = text.IndexOf(EscapedQuote, index + 2, StringComparison.Ordinal);
                if (close >= 0)
                {
                    index = close + 1;
                }
                else
                {
                    index++;
                }
            }

            index++;
        }

        return text.Length;
    }

    [GeneratedRegex("""^([ \t]*(?:set-)?cookie[ \t]*:)[^\r\n]*""", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CookieHeader();

    // Atomic prefix: once the scheme is consumed it can never be given back, so an already-masked line stays put.
    [GeneratedRegex(
        """((?>authorization[ \t]*[:=][ \t]*(?:[A-Za-z][A-Za-z0-9_\-]*[ \t]+)?))(?!\[REDACTED\][ \t]*(?:\r|\n|$))[^\r\n]+""",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex("""(bearer\s+)[A-Za-z0-9._~+/=\-]+""", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex("""<[A-Za-z][^<>]*>""")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(
        """(?<![A-Za-z0-9_\-])(?:name|id|property|http-equiv|itemprop)\s*=\s*(?:"(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s"'>]+))""",
        RegexOptions.IgnoreCase)]
    private static partial Regex NameAttribute();

    [GeneratedRegex("""(?<![A-Za-z0-9_\-])((?:value|content)\s*=\s*)(?:"[^"]*"|'[^']*'|[^\s"'>]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttribute();

    [GeneratedRegex("""token|session|csrf|xsrf|password|passwd|pwd|secret|api[-_]?key|authorization|cookie|credential""", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveWord();

    // The bounded tail keeps a failed attempt O(1), so long runs of word characters cannot go quadratic.
    [GeneratedRegex(
        """(?<key>token|session|csrf|xsrf|password|passwd|pwd|secret|api[-_]?key|authorization|cookie|credential)[A-Za-z0-9_\-]{0,40}(?<quote>\\?["'])?\s*(?<sep>[:=])\s*""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKey();

    // The lookbehind lets a match start only at the beginning of a run of address characters.
    [GeneratedRegex("""(?<![A-Z0-9._%+\-])[A-Z0-9._%+\-]+(?:@|%(?:25)*40)[A-Z0-9.\-]+\.[A-Z]{2,}""", RegexOptions.IgnoreCase)]
    private static partial Regex Email();

    // Four dotted decimal octets that are not part of a longer dotted/digit run (so 1.2.3 and dates stay).
    [GeneratedRegex("""(?<![0-9.])(?:(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(?![0-9]|\.[0-9])""")]
    private static partial Regex IPv4();

    // Full, compressed (::) and leading-:: forms. Needing either 8 groups or "::" keeps clock times and MAC addresses.
    [GeneratedRegex(
        """(?<![0-9A-Z:])(?:(?:[0-9A-F]{1,4}:){7}[0-9A-F]{1,4}|(?:[0-9A-F]{1,4}:){1,6}:[0-9A-F]{1,4}(?::[0-9A-F]{1,4}){0,5}|(?:[0-9A-F]{1,4}:){1,7}:|::[0-9A-F]{1,4}(?::[0-9A-F]{1,4}){0,6})(?![0-9A-Z:])""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IPv6();

    // 40 or more base64 characters starting at the beginning of a run. The digit/+/= requirement spares long
    // letters-and-slash file paths; random base64 of that length almost surely contains one.
    [GeneratedRegex("""(?<![A-Za-z0-9+/=])(?=[A-Za-z/]*[0-9+=])[A-Za-z0-9+/=]{40,}""")]
    private static partial Regex LongBase64Run();
}
