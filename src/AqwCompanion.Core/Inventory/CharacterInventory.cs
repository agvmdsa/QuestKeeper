using AqwCompanion.Core.Common;

namespace AqwCompanion.Core.Inventory;

/// <summary>One entry of the public inventory. For a class, <paramref name="Count"/> is its class points.</summary>
public sealed record InventoryItem(string Name, string Type, int Count, bool IsAc, bool IsMemberOnly);

/// <summary>How many distinct items of one type the inventory holds.</summary>
public sealed record InventoryTypeCount(string Type, int Count);

/// <summary>What the public inventory list shows for a character.</summary>
public sealed record CharacterInventory(IReadOnlyList<InventoryItem> Items)
{
    public const string OtherTypeName = "Other";
    public const string ClassTypeName = "Class";
    public const int PreviewTypeCount = 5;

    public int Count => Items.Count;

    /// <summary>Distinct items per type, most items first, then by type name; types are compared ignoring case.</summary>
    public IReadOnlyList<InventoryTypeCount> TypeCounts =>
        Items
            .GroupBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
            .Select(group => new InventoryTypeCount(group.First().Type, group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Kept outside the record so equality, copies and serialization never see it. Built once per instance, on first lookup.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterInventory, Dictionary<string, NameEntry>> Indexes = new();

    private readonly record struct NameEntry(InventoryItem First, long Total);

    private Dictionary<string, NameEntry> Index => Indexes.GetValue(this, static inventory =>
    {
        var index = new Dictionary<string, NameEntry>(inventory.Items.Count, StringComparer.Ordinal);
        foreach (var item in inventory.Items)
        {
            var key = NameKey.Normalize(item.Name);
            if (key.Length == 0)
            {
                continue;
            }

            index[key] = index.TryGetValue(key, out var entry)
                ? entry with { Total = entry.Total + item.Count }
                : new NameEntry(item, item.Count);
        }

        return index;
    });

    /// <summary>The summed counts of the items with this name (trimmed, case-insensitive); 0 when there is none.</summary>
    public int CountOf(string name) =>
        Index.TryGetValue(NameKey.Normalize(name), out var entry) ? SafeInt.Clamp(entry.Total) : 0;

    public bool Has(string name) => Find(name) is not null;

    /// <summary>The first item with this name (trimmed, case-insensitive), or null.</summary>
    public InventoryItem? Find(string name) =>
        Index.TryGetValue(NameKey.Normalize(name), out var entry) ? entry.First : null;
}
