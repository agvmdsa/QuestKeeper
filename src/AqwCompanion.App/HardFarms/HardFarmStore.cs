using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Wiki;
using Serilog;

namespace AqwCompanion.App.HardFarms;

/// <summary>
/// Maps the wiki data to two stored values. Plain DTOs are stored (the models carry computed members and a lookup table).
/// Store failures are logged by exception type only and swallowed.
/// </summary>
public sealed class HardFarmStore(IStoredDataStore store) : IHardFarmStore
{
    public const string ListKey = "hardfarms.list";
    public const string TypesKey = "hardfarms.types";

    public sealed record ListDto(List<GroupDto> Groups, string? Credit);

    public sealed record GroupDto(string Name, List<string> Slugs);

    public sealed record TypesDto(List<TypeRowDto> Rows);

    public sealed record TypeRowDto(string Slug, string Name, string Type);

    public StoredHardFarmList? LoadList() => Guard(nameof(LoadList), () =>
    {
        var stored = store.Load<ListDto>(ListKey);
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

    public StoredTypeTable? LoadTypes() => Guard(nameof(LoadTypes), () =>
    {
        var stored = store.Load<TypesDto>(TypesKey);
        if (stored?.Value is not { Rows: { } rows })
        {
            return null;
        }

        var table = new WikiTypeTable(rows
            .Where(r => r is not null && WikiUrls.IsValidSlug(r.Slug) && ParseType(r.Type) is not null)
            .Select(r => new WikiTypeRow(r.Slug, r.Name ?? string.Empty, ParseType(r.Type)!.Value)));
        return new StoredTypeTable(table, stored.ReadAtUtc);
    });

    public void SaveList(HardFarmList list, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveList), () =>
        {
            store.Save(
                ListKey,
                new ListDto(list.Groups.Select(g => new GroupDto(g.Name, g.Slugs.ToList())).ToList(), list.Credit),
                readAtUtc);
            return (object?)null;
        });

    public void SaveTypes(WikiTypeTable table, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveTypes), () =>
        {
            store.Save(
                TypesKey,
                new TypesDto(table.Rows.Select(r => new TypeRowDto(r.Slug, r.Name, r.Type.ToString())).ToList()),
                readAtUtc);
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
            Log.Error("Hard farm data operation {Operation} failed: {ExceptionType}", operation, ex.GetType().Name);
            return default;
        }
    }
}
