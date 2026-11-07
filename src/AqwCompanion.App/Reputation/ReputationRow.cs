using System.Globalization;
using AqwCompanion.Core.Reputation;

namespace AqwCompanion.App.Reputation;

/// <summary>One line of the Reputation list, with the numbers already formatted.</summary>
public sealed record ReputationRow(string Name, string Rank, string Rep, string ToNextRank)
{
    public static ReputationRow From(FactionStanding standing) => new(
        standing.Name,
        Format(standing.Rank),
        Format(standing.Rep),
        Format(standing.RepToRank));

    private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
