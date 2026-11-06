using AqwCompanion.Core.Wiki;

namespace AqwCompanion.App.HardFarms;

public sealed record StoredHardFarmList(HardFarmList List, DateTimeOffset ReadAtUtc);

/// <summary>The type table rebuilt from every stored index; <see cref="ReadAtUtc"/> is the newest read time among them.</summary>
public sealed record StoredTypeTable(WikiTypeTable Table, DateTimeOffset ReadAtUtc);

/// <summary>One parsed type index with the moment it was successfully read.</summary>
public sealed record StoredIndex(WikiIndex Index, DateTimeOffset ReadAtUtc);

/// <summary>Keeps the imported wiki data on disk. Never throws. The data lives in the account database, so Sign out and Forget remove it with the rest of the saved data (the Hard Farms tab is reset then).</summary>
public interface IHardFarmStore
{
    StoredHardFarmList? LoadList();

    /// <summary>Every stored type index (one per index page, distinct by slug); empty when none or unreadable.</summary>
    IReadOnlyList<StoredIndex> LoadIndexes();

    /// <summary>The type table rebuilt from <see cref="LoadIndexes"/> (a page in several indexes keeps its highest-priority type); null when there is no index.</summary>
    StoredTypeTable? LoadTypes();

    void SaveList(HardFarmList list, DateTimeOffset readAtUtc);

    /// <summary>Stores (or replaces) one index and its read time; the first save also removes the data of the old single type table.</summary>
    void SaveIndex(WikiIndex index, DateTimeOffset readAtUtc);
}
