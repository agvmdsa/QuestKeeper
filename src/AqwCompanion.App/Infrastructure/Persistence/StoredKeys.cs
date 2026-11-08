using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Persistence;

namespace AqwCompanion.App.Infrastructure.Persistence;

/// <summary>The only place stored records are declared: name, version of the stored shape, owning feature. Every key starts at version 1.</summary>
public static class StoredKeys
{
    public static readonly StoredKey<HardFarmStore.ListDto> HardFarmList = new("hardfarms.list", 1, "Hard Farms");

    public static readonly StoredKey<HardFarmStore.IndexesDto> HardFarmIndexes = new("hardfarms.indexes", 1, "Hard Farms");

    public static readonly StoredKey<GameDataStore.GameDataDto> GameData = new("gamedata.characters", 1, "Game data");

    /// <summary>Every key declared above.</summary>
    public static readonly IReadOnlyList<IStoredKey> All =
    [
        HardFarmList, HardFarmIndexes, GameData,
    ];

    /// <summary>Names of records no feature uses any more; removed after the first successful save.</summary>
    public static readonly IReadOnlyList<string> Obsolete = ["hardfarms.types", "reputation.data", "identity", "summary", "equipment", "badges", "inventory"];
}
