using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Persistence;

namespace AqwCompanion.App.Account;

/// <summary>
/// Maps the account state to five stored values (identity, summary, equipment, badges, inventory). Equipment is only restored for
/// the stored character, and badges and inventory only together with that equipment. Failures never reach the caller: the stored data module swallows and reports them.
/// </summary>
public sealed class AccountDataPersistence(StoredData data) : IAccountDataPersistence
{
    public RestoredAccount? Restore()
    {
        var identity = data.Load(StoredKeys.Identity);
        if (identity is null || string.IsNullOrWhiteSpace(identity.Value.CharacterName))
        {
            return null;
        }

        var name = identity.Value.CharacterName;
        var summary = data.Load(StoredKeys.Summary);
        var equipment = data.Load(StoredKeys.Equipment);
        if (equipment is not null
            && equipment.Value.Name is { } equipmentName
            && !equipmentName.EqualsIgnoreCase(name))
        {
            equipment = null;
        }

        var badges = equipment is null ? null : data.Load(StoredKeys.Badges);
        var inventory = equipment is null ? null : data.Load(StoredKeys.Inventory);

        return new RestoredAccount(
            name,
            summary?.Value,
            identity.ReadAtUtc,
            equipment?.Value,
            equipment?.ReadAtUtc,
            badges?.Value,
            badges?.ReadAtUtc,
            inventory?.Value,
            inventory?.ReadAtUtc);
    }

    public void SaveIdentity(string characterName, AccountSummary? summary, DateTimeOffset readAtUtc)
    {
        // Compared with what is on disk, not with memory: the equipment, badges and inventory there belong to the stored character.
        var stored = data.Load(StoredKeys.Identity);
        if (stored is null || !stored.Value.CharacterName.EqualsIgnoreCase(characterName))
        {
            data.Remove(StoredKeys.Equipment);
            data.Remove(StoredKeys.Badges);
            data.Remove(StoredKeys.Inventory);
        }

        data.Save(StoredKeys.Identity, new StoredName(characterName), readAtUtc);
        if (summary is null)
        {
            data.Remove(StoredKeys.Summary);
        }
        else
        {
            data.Save(StoredKeys.Summary, summary, readAtUtc);
        }
    }

    public void SaveEquipment(CharacterEquipment equipment, DateTimeOffset readAtUtc) =>
        data.Save(StoredKeys.Equipment, equipment, readAtUtc);

    public void SaveBadges(CharacterBadges badges, DateTimeOffset readAtUtc) =>
        data.Save(StoredKeys.Badges, badges, readAtUtc);

    public void SaveInventory(CharacterInventory inventory, DateTimeOffset readAtUtc) =>
        data.Save(StoredKeys.Inventory, inventory, readAtUtc);

    public void RemoveEquipment() => data.Remove(StoredKeys.Equipment);

    public void RemoveBadges() => data.Remove(StoredKeys.Badges);

    public void RemoveInventory() => data.Remove(StoredKeys.Inventory);

    public bool Forget() => data.Clear();
}
