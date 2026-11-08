using AqwCompanion.App.Account;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Reputation;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Persistence;

namespace AqwCompanion.App.Infrastructure.Persistence;

/// <summary>The only place stored records are declared: name, version of the stored shape, owning feature. Every key starts at version 1.</summary>
public static class StoredKeys
{
    public static readonly StoredKey<ReputationStore.ReputationDto> Reputation = new("reputation.data", 1, "Reputation");

    public static readonly StoredKey<HardFarmStore.ListDto> HardFarmList = new("hardfarms.list", 1, "Hard Farms");

    public static readonly StoredKey<HardFarmStore.IndexesDto> HardFarmIndexes = new("hardfarms.indexes", 1, "Hard Farms");

    public static readonly StoredKey<StoredName> Identity = new("identity", 1, "Account");

    public static readonly StoredKey<AccountSummary> Summary = new("summary", 1, "Account");

    public static readonly StoredKey<CharacterEquipment> Equipment = new("equipment", 1, "Account");

    public static readonly StoredKey<CharacterBadges> Badges = new("badges", 1, "Account");

    public static readonly StoredKey<CharacterInventory> Inventory = new("inventory", 1, "Account");

    /// <summary>Every key declared above.</summary>
    public static readonly IReadOnlyList<IStoredKey> All =
    [
        Reputation, HardFarmList, HardFarmIndexes, Identity, Summary, Equipment, Badges, Inventory,
    ];

    /// <summary>Names of records no feature uses any more; removed after the first successful save.</summary>
    public static readonly IReadOnlyList<string> Obsolete = ["hardfarms.types"];
}
