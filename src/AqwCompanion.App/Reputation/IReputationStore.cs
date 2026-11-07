using AqwCompanion.Core.Reputation;

namespace AqwCompanion.App.Reputation;

/// <summary>The imported reputation as it was kept: the standings, the name of the file they came from and the moment of the import.</summary>
public sealed record StoredReputation(ReputationSnapshot Snapshot, string SourceName, DateTimeOffset ImportedAt);

/// <summary>Keeps the imported reputation on disk. Never throws. The data lives in the account database, so Sign out and Forget remove it with the rest of the saved data (the Reputation tab is reset then).</summary>
public interface IReputationStore
{
    /// <summary>The stored reputation; null when there is none or it is unreadable.</summary>
    StoredReputation? Load();

    /// <summary>Stores the reputation, replacing the previous one.</summary>
    void Save(StoredReputation reputation);

    /// <summary>Deletes the stored reputation, if any.</summary>
    void Remove();
}
