using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.App.Persistence;

/// <summary>
/// Ties the in-memory ingestion state to the stored data: a restore seeds the drop-guard baseline, so the first refresh
/// of a session cannot replace good stored data with a degraded result, and a forget empties everything held in memory.
/// </summary>
public sealed class AccountSessionData(
    LatestResultStore<CharacterEquipment> equipment,
    LatestResultStore<CharacterBadges> badges,
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
    }

    public void Clear()
    {
        equipment.Clear();
        badges.Clear();
        identity.Clear();
        failures.Clear();
    }
}
