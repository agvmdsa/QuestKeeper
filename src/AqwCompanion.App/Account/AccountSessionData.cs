using AqwCompanion.App.Infrastructure;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.Account;

/// <summary>
/// Ties the in-memory ingestion state to the stored data: a restore seeds the drop-guard baseline, so the first refresh
/// of a session cannot replace good stored data with a degraded result, and a forget empties everything held in memory.
/// </summary>
public sealed class AccountSessionData(
    LatestResultStore<CharacterEquipment> equipment,
    LatestResultStore<CharacterBadges> badges,
    LatestResultStore<CharacterInventory> inventory,
    LatestResultStore<AccountIdentity> identity,
    FailedSnapshotBuffer failures) : IAccountSessionData
{
    public void Seed(RestoredAccount restored)
    {
        if (restored.Equipment is { } restoredEquipment)
        {
            equipment.SeedCount(restoredEquipment.Slots.Count);
        }

        if (restored.Badges is { } restoredBadges)
        {
            badges.SeedCount(restoredBadges.Count);
        }

        if (restored.Inventory is { } restoredInventory)
        {
            inventory.SeedCount(restoredInventory.Count);
        }
    }

    public void Clear()
    {
        equipment.Clear();
        badges.Clear();
        inventory.Clear();
        identity.Clear();
        failures.Clear();
    }
}
