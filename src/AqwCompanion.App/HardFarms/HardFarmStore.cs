using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Wiki;
using Serilog;

namespace AqwCompanion.App.HardFarms;

/// <summary>
/// Maps the wiki data to two stored values (the list, and the per-index type data). Plain DTOs are stored (the models carry computed members and a lookup table).
/// Store failures are logged by exception type only and swallowed.
/// </summary>
public sealed class HardFarmStore(IStoredDataStore store) : IHardFarmStore
{
    public const string ListKey = "hardfarms.list";
    public const string IndexesKey = "hardfarms.indexes";

    /// <summary>The key of the old single type table: ignored on load, removed on the first <see cref="SaveIndex"/>.</summary>
    public const string TypesKey = "hardfarms.types";

    public sealed record ListDto(List<GroupDto> Groups, string? Credit);

    public sealed record GroupDto(string Name, List<string> Slugs);

    public sealed record IndexesDto(List<IndexDto> Indexes);

    public sealed record IndexDto(string IndexSlug, string Type, List<EntryDto> Entries, DateTimeOffset ReadAtUtc);

    public sealed record EntryDto(string Slug, string Name);

    /// <summary>The shape of the old key; only its (empty) key is still referenced.</summary>
    public sealed record TypesDto(List<TypeRowDto> Rows);

    public sealed record TypeRowDto(string Slug, string Name, string Type);

    public StoredHardFarmList? LoadList() => Guard(nameof(LoadList), () =>
    {
        var stored = store.Load<ListDto>(ListKey, 1);
        if (stored?.Value is not { Groups: { } groups } dto)
        {
            return null;
        }

        var list = new HardFarmList(
            groups.Where(g => g is not null && g.Slugs is not null)
                .Select(g => new HardFarmGroup(g.Name ?? string.Empty, g.Slugs.Where(WikiUrls.IsValidSlug).ToList()))
                .ToList(),
            dto.Credit);
        return new StoredHardFarmList(list, stored.ReadAtUtc);
    });

    public IReadOnlyList<StoredIndex> LoadIndexes() => Guard(nameof(LoadIndexes), ReadIndexes) ?? [];

    public StoredTypeTable? LoadTypes() => Guard(nameof(LoadTypes), () =>
    {
        var indexes = ReadIndexes();
        return indexes.Count == 0
            ? null
            : new StoredTypeTable(WikiTypeTable.From(indexes.Select(i => i.Index)), indexes.Max(i => i.ReadAtUtc));
    });

    private List<StoredIndex> ReadIndexes()
    {
        var result = new List<StoredIndex>();
        if (store.Load<IndexesDto>(IndexesKey, 1)?.Value is not { Indexes: { } dtos })
        {
            return result;
        }

        foreach (var dto in dtos)
        {
            if (dto is null || !WikiUrls.IsTypeIndex(dto.IndexSlug ?? string.Empty) || ParseType(dto.Type) is not { } type)
            {
                continue;
            }

            var entries = (dto.Entries ?? [])
                .Where(e => e is not null && WikiUrls.IsValidSlug(e.Slug))
                .DistinctBy(e => e.Slug, StringComparer.Ordinal)
                .Select(e => new WikiIndexEntry(e.Slug, e.Name ?? string.Empty))
                .ToList();
            var stored = new StoredIndex(new WikiIndex(dto.IndexSlug!, type, entries), dto.ReadAtUtc);

            // A repeated index keeps its place but the last copy wins.
            var existing = result.FindIndex(i => i.Index.IndexSlug == stored.Index.IndexSlug);
            if (existing >= 0)
            {
                result[existing] = stored;
            }
            else
            {
                result.Add(stored);
            }
        }

        return result;
    }

    public void SaveList(HardFarmList list, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveList), () =>
        {
            store.Save(
                ListKey, 1,
                new ListDto(list.Groups.Select(g => new GroupDto(g.Name, g.Slugs.ToList())).ToList(), list.Credit),
                readAtUtc);
            return (object?)null;
        });

    public void SaveIndex(WikiIndex index, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveIndex), () =>
        {
            var all = ReadIndexes();
            var replacement = new StoredIndex(index, readAtUtc);
            var existing = all.FindIndex(i => i.Index.IndexSlug == index.IndexSlug);
            if (existing >= 0)
            {
                all[existing] = replacement;
            }
            else
            {
                all.Add(replacement);
            }

            store.Save(
                IndexesKey, 1,
                new IndexesDto(all.Select(i => new IndexDto(
                    i.Index.IndexSlug,
                    i.Index.Type.ToString(),
                    i.Index.Entries.Select(e => new EntryDto(e.Slug, e.Name)).ToList(),
                    i.ReadAtUtc)).ToList()),
                all.Max(i => i.ReadAtUtc));
            store.Remove(TypesKey);
            return (object?)null;
        });

    /// <summary>The type of a stored name; null for anything that is not exactly the name of a defined type (no numbers, lists or other case).</summary>
    private static WikiType? ParseType(string? text) =>
        text is { Length: > 0 }
        && Enum.TryParse<WikiType>(text, ignoreCase: false, out var type)
        && Enum.IsDefined(type)
        && string.Equals(Enum.GetName(type), text, StringComparison.Ordinal)
            ? type
            : null;

    private static T? Guard<T>(string operation, Func<T?> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Hard farm data operation {SafeOperation} failed: {ExceptionType}", operation, ex.GetType().Name);
            return default;
        }
    }
}
