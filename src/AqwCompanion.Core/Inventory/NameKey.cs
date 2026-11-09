using System.Text;

namespace AqwCompanion.Core.Inventory;

/// <summary>
/// The key two item names are compared by: trimmed, runs of whitespace collapsed to one space, case ignored.
/// Nothing smarter on purpose; the rules for matching wiki names are decided where they are needed.
/// </summary>
public static class NameKey
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The looser key used to match wiki names and page names: only lower-case letters and digits (Unicode letters are kept),
    /// everything else dropped. "Evolved Shadow of Nulgath" and "evolved-shadow-of-nulgath" give the same key.
    /// </summary>
    public static string Compact(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    // The tags a trailing "(...)" may carry that say how an item is obtained or in which edition, not which item it is.
    private static readonly HashSet<string> Qualifiers = new(StringComparer.Ordinal)
    {
        "class", "merge", "quest", "daily quest", "ac", "0 ac", "non-ac", "non ac",
    };

    /// <summary>
    /// The key two names of the same item share: <see cref="Compact"/> after every trailing "(Class)", "(Merge)", "(Quest)",
    /// "(Daily Quest)", "(AC)", "(0 AC)" and "(Non-AC)" is dropped, however many there are. Any other qualifier, and any in the
    /// middle of the name, is kept because it tells different items apart. Empty when nothing but qualifiers is left.
    /// </summary>
    public static string Base(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var rest = name.AsSpan().Trim();
        while (rest.Length > 0 && rest[^1] == ')')
        {
            var open = rest.LastIndexOf('(');
            if (open < 0)
            {
                break;
            }

            var tag = Normalize(rest[(open + 1)..^1].ToString());
            if (!Qualifiers.Contains(tag))
            {
                break;
            }

            rest = rest[..open].TrimEnd();
        }

        return Compact(rest.ToString());
    }
}
