namespace AqwCompanion.Core.Reputation;

/// <summary>The valid faction standings of one reputation file and the export time the file stated (cleaned text, not parsed).</summary>
public sealed record ReputationSnapshot(IReadOnlyList<FactionStanding> Factions, string? ExportedAt);
