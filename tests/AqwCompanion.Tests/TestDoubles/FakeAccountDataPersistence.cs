using AqwCompanion.App.Account;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Records every call as text such as "SaveIdentity:Test Hero" so tests can assert order.</summary>
public sealed class FakeAccountDataPersistence : IAccountDataPersistence
{
    public List<string> Calls { get; } = [];

    public List<(string Name, AccountSummary? Summary, DateTimeOffset At)> SavedIdentities { get; } = [];

    public List<(CharacterEquipment Equipment, DateTimeOffset At)> SavedEquipment { get; } = [];

    public List<(CharacterBadges Badges, DateTimeOffset At)> SavedBadges { get; } = [];

    /// <summary>What <see cref="Restore"/> returns.</summary>
    public RestoredAccount? ToRestore { get; set; }

    /// <summary>When set, <see cref="Restore"/> blocks on it so tests can interleave other work.</summary>
    public Task? RestoreGate { get; set; }

    public RestoredAccount? Restore()
    {
        Calls.Add("Restore");
        RestoreGate?.Wait();
        return ToRestore;
    }

    public void SaveIdentity(string characterName, AccountSummary? summary, DateTimeOffset readAtUtc)
    {
        Calls.Add("SaveIdentity:" + characterName);
        SavedIdentities.Add((characterName, summary, readAtUtc));
    }

    public void SaveEquipment(CharacterEquipment equipment, DateTimeOffset readAtUtc)
    {
        Calls.Add("SaveEquipment:" + equipment.Name);
        SavedEquipment.Add((equipment, readAtUtc));
    }

    public void SaveBadges(CharacterBadges badges, DateTimeOffset readAtUtc)
    {
        Calls.Add("SaveBadges:" + badges.Count);
        SavedBadges.Add((badges, readAtUtc));
    }

    public void RemoveEquipment() => Calls.Add("RemoveEquipment");

    public void RemoveBadges() => Calls.Add("RemoveBadges");

    /// <summary>What <see cref="Forget"/> returns.</summary>
    public bool ForgetResult { get; set; } = true;

    public bool Forget()
    {
        Calls.Add("Forget");
        return ForgetResult;
    }
}

/// <summary>Records the calls the view model makes to the session-only data.</summary>
public sealed class FakeAccountSessionData : IAccountSessionData
{
    public List<RestoredAccount> Seeded { get; } = [];

    public int ClearCalls { get; private set; }

    public void Seed(RestoredAccount restored) => Seeded.Add(restored);

    public void Clear() => ClearCalls++;
}

/// <summary>A clock that always reports the same instant.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
