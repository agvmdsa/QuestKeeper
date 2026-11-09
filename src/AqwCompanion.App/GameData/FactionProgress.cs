using AqwCompanion.Core.Reputation;

namespace AqwCompanion.App.GameData;

/// <summary>How far a faction standing is from its next rank, shared by the dashboard and the local feed.</summary>
public static class FactionProgress
{
    public const int MaxRank = 10;

    public static bool IsMax(FactionStanding f) => f.Rank >= MaxRank;

    /// <summary>What is still missing for the next rank; a faction at the maximum rank has nothing to earn and sorts last.</summary>
    public static long Missing(FactionStanding f) =>
        IsMax(f) ? long.MaxValue : Math.Max(0, (long)f.RepToRank - f.SpillRep);

    /// <summary>The share of the current rank already earned, from 0 to 1 (1 at the maximum rank).</summary>
    public static double Fraction(FactionStanding f) =>
        IsMax(f) ? 1 : f.RepToRank <= 0 ? 0 : Math.Clamp((double)f.SpillRep / f.RepToRank, 0, 1);
}
