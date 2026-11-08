namespace AqwCompanion.App.GameData;

/// <summary>
/// One faction standing, with the numbers already formatted ("N0", invariant culture). <paramref name="Fraction"/> is the share of the
/// current rank already earned, from 0 to 1 (1 at the maximum rank, where <paramref name="IsMax"/> is true).
/// </summary>
public sealed record GameFactionRow(string Name, string Rank, string Rep, string Progress, string ToNextRank, double Fraction, bool IsMax)
{
    /// <summary>What is missing for the next rank in words, or that the rank is the highest.</summary>
    public string NextText => IsMax ? "Max rank" : ToNextRank + " to next";
}
