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
/// Matches a wiki display name against the public inventory: exact on <see cref="NameKey"/>, then once more without ONE trailing
/// "(Class)". Other qualifiers such as "(AC)" or "(0 AC)" are never stripped: they tell different items apart.
/// </summary>
public static class OwnershipMatcher
{
    private const string ClassQualifier = "(class)";

    public static Ownership Match(string? wikiName, CharacterInventory? inventory)
    {
        if (inventory is null || string.IsNullOrWhiteSpace(wikiName))
        {
            return Ownership.Unknown;
        }

        if (inventory.Has(wikiName))
        {
            return Ownership.Owned;
        }

        var trimmed = wikiName.TrimEnd();
        if (trimmed.EndsWith(ClassQualifier, StringComparison.OrdinalIgnoreCase))
        {
            var stripped = trimmed[..^ClassQualifier.Length].TrimEnd();
            if (stripped.Length > 0 && inventory.Has(stripped))
            {
                return Ownership.Owned;
            }
        }

        return Ownership.Missing;
    }
}
