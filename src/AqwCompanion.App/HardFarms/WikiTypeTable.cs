using AqwCompanion.Core.Wiki;

namespace AqwCompanion.App.HardFarms;

public sealed record WikiTypeRow(string Slug, string Name, WikiType Type);

/// <summary>The item name and type of every wiki page found in the type indexes; a page in several indexes keeps the highest-priority type.</summary>
public sealed class WikiTypeTable
{
    public static readonly WikiTypeTable Empty = new([]);

    private readonly Dictionary<string, WikiTypeRow> _bySlug;

    /// <summary>Rows are distinct by slug; when the same slug appears twice, the better type (and its name) wins.</summary>
    public WikiTypeTable(IEnumerable<WikiTypeRow> rows)
    {
        _bySlug = new Dictionary<string, WikiTypeRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (_bySlug.TryGetValue(row.Slug, out var existing))
            {
                if (row.Type < existing.Type)
                {
                    _bySlug[row.Slug] = row;
                }
            }
            else
            {
                _bySlug[row.Slug] = row;
            }
        }
    }

    public int Count => _bySlug.Count;

    public IReadOnlyCollection<WikiTypeRow> Rows => _bySlug.Values;

    public WikiTypeRow? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    public static WikiTypeTable From(IEnumerable<WikiIndex> indexes) =>
        new(indexes.SelectMany(index => index.Entries.Select(entry => new WikiTypeRow(entry.Slug, entry.Name, index.Type))));
}
