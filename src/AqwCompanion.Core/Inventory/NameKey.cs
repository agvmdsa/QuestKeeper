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
}
