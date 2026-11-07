namespace AqwCompanion.Core.Reputation;

/// <summary>The standing of the character with one faction, as written in the imported reputation file.</summary>
public sealed record FactionStanding(int FactionId, string Name, int Rank, int Rep, int RepToRank, int SpillRep);
