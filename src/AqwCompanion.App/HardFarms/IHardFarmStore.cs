using AqwCompanion.Core.Wiki;

namespace AqwCompanion.App.HardFarms;

public sealed record StoredHardFarmList(HardFarmList List, DateTimeOffset ReadAtUtc);

public sealed record StoredTypeTable(WikiTypeTable Table, DateTimeOffset ReadAtUtc);

/// <summary>Keeps the imported wiki data on disk. Never throws. The data lives in the account database, so Sign out and Forget remove it with the rest of the saved data (the Hard Farms tab is reset then).</summary>
public interface IHardFarmStore
{
    StoredHardFarmList? LoadList();

    StoredTypeTable? LoadTypes();

    void SaveList(HardFarmList list, DateTimeOffset readAtUtc);

    void SaveTypes(WikiTypeTable table, DateTimeOffset readAtUtc);
}
