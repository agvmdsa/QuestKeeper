using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Reputation;

namespace AqwCompanion.App.Reputation;

/// <summary>
/// Maps the imported reputation to one stored value. Plain DTOs are stored; the import time is the stored read time.
/// Failures never reach the caller: the stored data module swallows and reports them.
/// </summary>
public sealed class ReputationStore(StoredData data) : IReputationStore
{
    public sealed record ReputationDto(List<FactionDto> Factions, string? ExportedAt, string SourceName);

    public sealed record FactionDto(int FactionId, string Name, int Rank, int Rep, int RepToRank, int SpillRep);

    public StoredReputation? Load()
    {
        var stored = data.Load(StoredKeys.Reputation);
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
    }

    public void Save(StoredReputation reputation) =>
        data.Save(
            StoredKeys.Reputation,
            new ReputationDto(
                reputation.Snapshot.Factions
                    .Select(f => new FactionDto(f.FactionId, f.Name, f.Rank, f.Rep, f.RepToRank, f.SpillRep))
                    .ToList(),
                reputation.Snapshot.ExportedAt,
                reputation.SourceName),
            reputation.ImportedAt);

    public void Remove() => data.Remove(StoredKeys.Reputation);
}
