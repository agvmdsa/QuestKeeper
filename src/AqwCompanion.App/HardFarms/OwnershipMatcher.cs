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
/// Matches a Hard Farming entry against the inventory by name keys, whole key against whole key (never a part: "void-paladin"
/// is not "cyber-void-paladin"). The wiki name is tried as it is and without its trailing acquisition tags ("(Class)",
/// "(Merge)", "(Quest)", "(Daily Quest)") and AC tags ("(AC)", "(0 AC)", "(Non-AC)"): owning the AC or the non-AC version is
/// owning the item. The inventory side is stripped the same way. Other qualifiers such as "(Rare)" tell items apart and stay.
/// When the wiki name is unknown the page name is used, with its trailing page-type words removed one at a time.
/// </summary>
public static class OwnershipMatcher
{
    private static readonly string[] PageTypeWords = ["classmerge", "class", "quest", "merge"];

    /// <summary><see cref="Ownership.Unknown"/> only when the inventory is not loaded; otherwise Owned or Missing.</summary>
    public static Ownership Match(string slug, string? wikiName, CharacterInventory? inventory)
    {
        if (inventory is null)
        {
            return Ownership.Unknown;
        }

        foreach (var key in KeysTried(slug, wikiName))
        {
            if (inventory.HasCompactKey(key))
            {
                return Ownership.Owned;
            }
        }

        // The item may carry the tags itself ("Test Blade (AC)" in the bag, "Test Blade" on the wiki).
        return !string.IsNullOrWhiteSpace(wikiName) && inventory.HasBaseKey(wikiName) ? Ownership.Owned : Ownership.Missing;
    }

    /// <summary>The compact keys <see cref="Match"/> looks for, in order and without repeats. Shown in the debug report.</summary>
    public static IReadOnlyList<string> KeysTried(string? slug, string? wikiName)
    {
        var keys = new List<string>();

        void Add(string key)
        {
            if (key.Length > 0 && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        if (!string.IsNullOrWhiteSpace(wikiName))
        {
            Add(NameKey.Compact(wikiName));
            Add(NameKey.Base(wikiName));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return keys;
        }

        Add(NameKey.Compact(slug));

        var words = slug.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var count = words.Length;
        while (count > 1 && PageTypeWords.Contains(words[count - 1], StringComparer.OrdinalIgnoreCase))
        {
            count--;
            Add(NameKey.Compact(string.Concat(words[..count])));
        }

        return keys;
    }
}
