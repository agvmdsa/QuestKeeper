using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Reputation;
using Serilog;

namespace AqwCompanion.App.Reputation;

/// <summary>
/// Maps the imported reputation to one stored value. Plain DTOs are stored; the import time is the stored read time.
/// Store failures are logged by exception type only and swallowed.
/// </summary>
public sealed class ReputationStore(IStoredDataStore store) : IReputationStore
{
    public const string Key = "reputation.data";

    public sealed record ReputationDto(List<FactionDto> Factions, string? ExportedAt, string SourceName);

    public sealed record FactionDto(int FactionId, string Name, int Rank, int Rep, int RepToRank, int SpillRep);

    public StoredReputation? Load() => Guard(nameof(Load), () =>
    {
        var stored = store.Load<ReputationDto>(Key, 1);
        if (stored?.Value is not { Factions: { } dtos } dto)
        {
            return null;
        }

        var factions = dtos
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name)
                && f.FactionId >= 0 && f.Rank >= 0 && f.Rep >= 0 && f.RepToRank >= 0 && f.SpillRep >= 0)
            .DistinctBy(f => f.FactionId)
            .Select(f => new FactionStanding(f.FactionId, f.Name, f.Rank, f.Rep, f.RepToRank, f.SpillRep))
            .ToList();
        return factions.Count == 0
            ? null
            : new StoredReputation(new ReputationSnapshot(factions, dto.ExportedAt), dto.SourceName ?? string.Empty, stored.ReadAtUtc);
    });

    public void Save(StoredReputation reputation) => Guard(nameof(Save), () =>
    {
        store.Save(
            Key, 1,
            new ReputationDto(
                reputation.Snapshot.Factions
                    .Select(f => new FactionDto(f.FactionId, f.Name, f.Rank, f.Rep, f.RepToRank, f.SpillRep))
                    .ToList(),
                reputation.Snapshot.ExportedAt,
                reputation.SourceName),
            reputation.ImportedAt);
        return (object?)null;
    });

    public void Remove() => Guard(nameof(Remove), () =>
    {
        store.Remove(Key);
        return (object?)null;
    });

    private static T? Guard<T>(string operation, Func<T?> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Reputation data operation {SafeOperation} failed: {ExceptionType}", operation, ex.GetType().Name);
            return default;
        }
    }
}
