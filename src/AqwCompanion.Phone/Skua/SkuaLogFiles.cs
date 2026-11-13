using System.Security.Cryptography;
using System.Text;

namespace AqwCompanion.Phone.Skua;

/// <summary>The file names of the per-character Skua logs.</summary>
public static class SkuaLogFiles
{
    public const string Prefix = "skua-log.";
    public const string Suffix = ".json";

    /// <summary>The single log of older versions; never produced by <see cref="FileName"/>.</summary>
    public const string LegacyFileName = "skua-log.json";

    private const int MaxNameLength = 48;

    /// <summary>
    /// <c>skua-log.&lt;name&gt;.json</c>: the character name in lower case (names are case-insensitive in the game) with only
    /// ASCII letters, digits, <c>-</c> and <c>_</c>. Any other character becomes <c>_</c>, a long name is cut, and when
    /// anything was changed a short hash of the whole name is added, so two names never share a file in practice. The result
    /// is a bare file name: no separators, no dots inside the name part.
    /// </summary>
    public static string FileName(string character)
    {
        var lower = character.ToLowerInvariant();
        var name = new StringBuilder(Math.Min(lower.Length, MaxNameLength));
        var changed = lower.Length == 0 || lower.Length > MaxNameLength;
        foreach (var c in lower.Take(MaxNameLength))
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_')
            {
                name.Append(c);
            }
            else
            {
                name.Append('_');
                changed = true;
            }
        }

        if (changed)
        {
            name.Append('-').Append(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lower)))[..8]);
        }

        return Prefix + name + Suffix;
    }
}
