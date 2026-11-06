using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;

namespace AqwCompanion.App.Persistence;

/// <summary>The stored character name; the one piece of identity kept besides the account summary.</summary>
public sealed record StoredName(string CharacterName);

/// <summary>What was found on disk at startup, with the time each part was read from the site.</summary>
public sealed record RestoredAccount(
    string CharacterName,
    AccountSummary? Summary,
    DateTimeOffset IdentityReadAtUtc,
    CharacterEquipment? Equipment,
    DateTimeOffset? EquipmentReadAtUtc,
    CharacterBadges? Badges,
    DateTimeOffset? BadgesReadAtUtc);

/// <summary>The in-memory data of this session that has to follow what was restored or forgotten.</summary>
public interface IAccountSessionData
{
    /// <summary>Gives the ingestion drop guard the item counts of the restored data as its starting point.</summary>
    void Seed(RestoredAccount restored);

    /// <summary>Drops the latest parsed values and the raw HTML of the last failed page.</summary>
    void Clear();
}

/// <summary>Keeps the last successfully read account data on disk. Never throws; failures are logged by type only.</summary>
public interface IAccountDataPersistence
{
    /// <summary>The stored account, or null when nothing usable is stored.</summary>
    RestoredAccount? Restore();

    void SaveIdentity(string characterName, AccountSummary? summary, DateTimeOffset readAtUtc);

    void SaveEquipment(CharacterEquipment equipment, DateTimeOffset readAtUtc);

    void SaveBadges(CharacterBadges badges, DateTimeOffset readAtUtc);

    void RemoveEquipment();

    void RemoveBadges();

    /// <summary>Deletes everything that is stored; true only when all of it is gone.</summary>
    bool Forget();
}
