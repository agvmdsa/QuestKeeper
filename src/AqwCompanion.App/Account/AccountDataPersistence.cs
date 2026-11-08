using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Persistence;
using Serilog;

namespace AqwCompanion.App.Account;

/// <summary>
/// Maps the account state to five stored values (identity, summary, equipment, badges, inventory). Equipment is only restored for
/// the stored character, and badges and inventory only together with that equipment. Store failures are logged and swallowed.
/// </summary>
public sealed class AccountDataPersistence(IStoredDataStore store) : IAccountDataPersistence
{
    public const string IdentityKey = "identity";
    public const string SummaryKey = "summary";
    public const string EquipmentKey = "equipment";
    public const string BadgesKey = "badges";
    public const string InventoryKey = "inventory";

    public RestoredAccount? Restore()
    {
        try
        {
            var identity = store.Load<StoredName>(IdentityKey, 1);
            if (identity is null || string.IsNullOrWhiteSpace(identity.Value.CharacterName))
            {
                return null;
            }

            var name = identity.Value.CharacterName;
            var summary = store.Load<AccountSummary>(SummaryKey, 1);
            var equipment = store.Load<CharacterEquipment>(EquipmentKey, 1);
            if (equipment is not null
                && equipment.Value.Name is { } equipmentName
                && !equipmentName.EqualsIgnoreCase(name))
            {
                equipment = null;
            }

            var badges = equipment is null ? null : store.Load<CharacterBadges>(BadgesKey, 1);
            var inventory = equipment is null ? null : store.Load<CharacterInventory>(InventoryKey, 1);

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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogFailure(nameof(Restore), ex);
            return null;
        }
    }

    public void SaveIdentity(string characterName, AccountSummary? summary, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveIdentity), () =>
        {
            // Compared with what is on disk, not with memory: the equipment, badges and inventory there belong to the stored character.
            var stored = store.Load<StoredName>(IdentityKey, 1);
            if (stored is null || !stored.Value.CharacterName.EqualsIgnoreCase(characterName))
            {
                store.Remove(EquipmentKey);
                store.Remove(BadgesKey);
                store.Remove(InventoryKey);
            }

            store.Save(IdentityKey, 1, new StoredName(characterName), readAtUtc);
            if (summary is null)
            {
                store.Remove(SummaryKey);
            }
            else
            {
                store.Save(SummaryKey, 1, summary, readAtUtc);
            }
        });

    public void SaveEquipment(CharacterEquipment equipment, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveEquipment), () => store.Save(EquipmentKey, 1, equipment, readAtUtc));

    public void SaveBadges(CharacterBadges badges, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveBadges), () => store.Save(BadgesKey, 1, badges, readAtUtc));

    public void SaveInventory(CharacterInventory inventory, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveInventory), () => store.Save(InventoryKey, 1, inventory, readAtUtc));

    public void RemoveEquipment() => Guard(nameof(RemoveEquipment), () => store.Remove(EquipmentKey));

    public void RemoveBadges() => Guard(nameof(RemoveBadges), () => store.Remove(BadgesKey));

    public void RemoveInventory() => Guard(nameof(RemoveInventory), () => store.Remove(InventoryKey));

    public bool Forget()
    {
        try
        {
            return store.Clear();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogFailure(nameof(Forget), ex);
            return false;
        }
    }

    private static void Guard(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogFailure(operation, ex);
        }
    }

    private static void LogFailure(string operation, Exception ex) =>
        Log.Error("Stored data operation {SafeOperation} failed: {ExceptionType}", operation, ex.GetType().Name);
}
