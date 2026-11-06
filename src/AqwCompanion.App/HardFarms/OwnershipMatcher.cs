using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.HardFarms;

/// <summary>Whether the character owns a Hard Farming entry.</summary>
public enum Ownership
{
    Owned,
    Missing,
    Unknown,
}

/// <summary>
/// Matches a Hard Farming entry against the public inventory by <see cref="NameKey.Compact"/> keys, whole key against whole
/// key (never a part: "void-paladin" is not "cyber-void-paladin"). Candidate names, in order: the wiki display name (and,
/// without ONE trailing "(Class)"), the page name, the page name without ONE trailing page-type word. Other qualifiers such
/// as "(AC)" or "(0 AC)" are never stripped: they tell different items apart.
/// </summary>
public static class OwnershipMatcher
{
    private const string ClassQualifier = "(class)";

    private static readonly string[] PageTypeWords = ["classmerge", "class", "quest", "merge"];

    /// <summary><see cref="Ownership.Unknown"/> only when the inventory is not loaded; otherwise Owned or Missing.</summary>
    public static Ownership Match(string slug, string? wikiName, CharacterInventory? inventory)
    {
        if (inventory is null)
        {
            return Ownership.Unknown;
        }

        foreach (var candidate in Candidates(slug, wikiName))
        {
            if (inventory.HasCompactKey(candidate))
            {
                return Ownership.Owned;
            }
        }

        return Ownership.Missing;
    }

    private static IEnumerable<string> Candidates(string? slug, string? wikiName)
    {
        if (!string.IsNullOrWhiteSpace(wikiName))
        {
            yield return NameKey.Compact(wikiName);

            var trimmed = wikiName.TrimEnd();
            if (trimmed.EndsWith(ClassQualifier, StringComparison.OrdinalIgnoreCase))
            {
                yield return NameKey.Compact(trimmed[..^ClassQualifier.Length]);
            }
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            yield break;
        }

        yield return NameKey.Compact(slug);

        var words = slug.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && PageTypeWords.Contains(words[^1], StringComparer.OrdinalIgnoreCase))
        {
            yield return NameKey.Compact(string.Concat(words[..^1]));
        }
    }
}
