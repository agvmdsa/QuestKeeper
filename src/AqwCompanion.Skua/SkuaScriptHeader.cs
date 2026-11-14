using System.Globalization;
using System.Text;

namespace AqwCompanion.Skua;

/// <summary>
/// Reads the header comment most bot scripts start with: a first block comment holding <c>name:</c>, <c>description:</c>
/// and <c>tags:</c> lines. Pure and defensive (the plugin feeds it files from the user's disk): only the first
/// <see cref="ScanChars"/> chars are looked at and nothing here throws.
/// </summary>
public static class SkuaScriptHeader
{
    /// <summary>How many chars of the file are scanned; a header ending after this is treated as not there.</summary>
    public const int ScanChars = 4 * 1024;

    public static (string? Title, string? Description, string? Tags) Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (null, null, null);
        }

        var scanned = text.Length > ScanChars ? text[..ScanChars] : text;
        var start = 0;
        while (start < scanned.Length && (scanned[start] == '\uFEFF' || char.IsWhiteSpace(scanned[start])))
        {
            start++;
        }

        if (!scanned.AsSpan(start).StartsWith("/*", StringComparison.Ordinal))
        {
            return (null, null, null);
        }

        var end = scanned.IndexOf("*/", start + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            return (null, null, null);
        }

        string? title = null, description = null, tags = null;
        var seen = 0;
        foreach (var raw in scanned[(start + 2)..end].Split('\n'))
        {
            var colon = raw.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = raw[..colon].Trim();
            var value = raw[(colon + 1)..];
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && (seen & 1) == 0)
            {
                seen |= 1;
                title = Clean(value, SkuaProtocol.MaxScriptTitleLength);
            }
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase) && (seen & 2) == 0)
            {
                seen |= 2;
                description = Clean(value, SkuaProtocol.MaxScriptDescriptionLength);
            }
            else if (key.Equals("tags", StringComparison.OrdinalIgnoreCase) && (seen & 4) == 0)
            {
                seen |= 4;
                tags = Clean(value, SkuaProtocol.MaxScriptTagsLength);
            }
        }

        return (title, description, tags);
    }

    /// <summary>Control characters become a space, format characters (bidi marks, zero-width, BOM) and lone surrogates go, the text is trimmed and cut (no split pair); empty is null.</summary>
    public static string? Clean(string value, int max)
    {
        var chars = new StringBuilder(value.Length);
        foreach (var c in SkuaText.WithoutLoneSurrogates(value))
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                continue;
            }

            chars.Append(char.IsControl(c) ? ' ' : c);
        }

        var cut = SkuaText.Cut(chars.ToString().Trim(), max).Trim();
        return cut.Length == 0 ? null : cut;
    }
}
