using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Persistence;
using Serilog;

namespace AqwCompanion.App.Persistence;

/// <summary>
/// Maps the account state to four stored values (identity, summary, equipment, badges). Equipment is only restored for
/// the stored character, and badges only together with that equipment. Store failures are logged and swallowed.
/// </summary>
public sealed class AccountDataPersistence(IStoredDataStore store) : IAccountDataPersistence
{
    public const string IdentityKey = "identity";
    public const string SummaryKey = "summary";
    public const string EquipmentKey = "equipment";
    public const string BadgesKey = "badges";

    public RestoredAccount? Restore()
    {
        try
        {
            var identity = store.Load<StoredName>(IdentityKey);
            if (identity is null || string.IsNullOrWhiteSpace(identity.Value.CharacterName))
            {
                return null;
            }

            var name = identity.Value.CharacterName;
            var summary = store.Load<AccountSummary>(SummaryKey);
            var equipment = store.Load<CharacterEquipment>(EquipmentKey);
            if (equipment is not null
                && equipment.Value.Name is { } equipmentName
                && !string.Equals(equipmentName, name, StringComparison.OrdinalIgnoreCase))
            {
                equipment = null;
            }

            var badges = equipment is null ? null : store.Load<CharacterBadges>(BadgesKey);

            return new RestoredAccount(
                name,
                summary?.Value,
                identity.ReadAtUtc,
                equipment?.Value,
                equipment?.ReadAtUtc,
                badges?.Value,
                badges?.ReadAtUtc);
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
            // Compared with what is on disk, not with memory: the equipment and badges there belong to the stored character.
            var stored = store.Load<StoredName>(IdentityKey);
            if (stored is null || !string.Equals(stored.Value.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
            {
                store.Remove(EquipmentKey);
                store.Remove(BadgesKey);
            }

            store.Save(IdentityKey, new StoredName(characterName), readAtUtc);
            if (summary is null)
            {
                store.Remove(SummaryKey);
            }
            else
            {
                store.Save(SummaryKey, summary, readAtUtc);
            }
        });

    public void SaveEquipment(CharacterEquipment equipment, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveEquipment), () => store.Save(EquipmentKey, equipment, readAtUtc));

    public void SaveBadges(CharacterBadges badges, DateTimeOffset readAtUtc) =>
        Guard(nameof(SaveBadges), () => store.Save(BadgesKey, badges, readAtUtc));

    public void RemoveEquipment() => Guard(nameof(RemoveEquipment), () => store.Remove(EquipmentKey));

    public void RemoveBadges() => Guard(nameof(RemoveBadges), () => store.Remove(BadgesKey));

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
        Log.Error("Stored data operation {Operation} failed: {ExceptionType}", operation, ex.GetType().Name);
}
