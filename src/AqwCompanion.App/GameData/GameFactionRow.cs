namespace AqwCompanion.App.GameData;

/// <summary>One faction standing of the Game data list, with the numbers already formatted ("N0", invariant culture).</summary>
public sealed record GameFactionRow(string Name, string Rank, string Rep, string Progress, string ToNextRank);
