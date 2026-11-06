namespace AqwCompanion.Core.Badges;

/// <summary>One badge from the public badges list. Only text is kept; the badge image is never read.</summary>
public sealed record Badge(string Title, string Description, string Category);

/// <summary>The badges of one category, in the order the list gave them.</summary>
public sealed record BadgeGroup(string Category, IReadOnlyList<Badge> Badges);

/// <summary>What the public badges list shows for a character.</summary>
public sealed record CharacterBadges(IReadOnlyList<Badge> Badges)
{
    public const string UncategorizedName = "Other";
    public const int PreviewCount = 5;

    public int Count => Badges.Count;

    /// <summary>The first few badges, shown on the dashboard card.</summary>
    public IEnumerable<Badge> Preview => Badges.Take(PreviewCount);

    /// <summary>Badges grouped by category; categories and badges keep the order in which they first appear.</summary>
    public IReadOnlyList<BadgeGroup> GroupedByCategory =>
        Badges
            .GroupBy(badge => badge.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BadgeGroup(group.First().Category, group.ToList()))
            .ToList();
}
