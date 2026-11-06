namespace AqwCompanion.Core.Wiki;

/// <summary>One entry of the Hard Farming list: the wiki page name and the group it was listed under.</summary>
public sealed record HardFarmEntry(string Slug, string Group);

public sealed record HardFarmGroup(string Name, IReadOnlyList<string> Slugs);

/// <summary>The imported list. <see cref="Entries"/> is flattened, distinct by slug (the first group wins).</summary>
public sealed record HardFarmList(IReadOnlyList<HardFarmGroup> Groups, string? Credit)
{
    public IReadOnlyList<HardFarmEntry> Entries { get; } = Flatten(Groups);

    public int Count => Entries.Count;

    private static List<HardFarmEntry> Flatten(IReadOnlyList<HardFarmGroup> groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<HardFarmEntry>();
        foreach (var group in groups)
        {
            foreach (var slug in group.Slugs)
            {
                if (seen.Add(slug))
                {
                    entries.Add(new HardFarmEntry(slug, group.Name));
                }
            }
        }

        return entries;
    }
}
