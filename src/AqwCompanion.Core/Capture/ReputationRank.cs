using AqwCompanion.Core.Common;
using AqwCompanion.Core.Reputation;

namespace AqwCompanion.Core.Capture;

/// <summary>The Wiki's reputation rank table: the rank and the progress inside it, from the total reputation of a faction.</summary>
public static class ReputationRank
{
    // Combined totals at which ranks 2-10 start.
    private static readonly long[] RankStarts = [900, 3600, 10000, 22500, 44100, 78400, 129600, 202500, 302500];

    // The increment each of ranks 1-9 needs to reach the next one.
    private static readonly int[] Increments = [900, 2700, 6400, 12500, 21600, 34300, 51200, 72900, 100000];

    /// <summary>
    /// Rank 1-10 from the total. Rep is the total (clamped to int, never negative); SpillRep is the total minus the combined
    /// total of the current rank; RepToRank is the increment the current rank needs, 0 at rank 10 (SpillRep 0 there too).
    /// </summary>
    public static FactionStanding Standing(int factionId, string name, long totalRep)
    {
        var total = Math.Max(0, totalRep);
        var index = 0;
        while (index < RankStarts.Length && total >= RankStarts[index])
        {
            index++;
        }

        var rank = index + 1;
        if (rank == RankStarts.Length + 1)
        {
            return new FactionStanding(factionId, name, rank, SafeInt.Clamp(total), 0, 0);
        }

        var rankStart = index == 0 ? 0 : RankStarts[index - 1];
        return new FactionStanding(factionId, name, rank, SafeInt.Clamp(total), Increments[index], SafeInt.Clamp(total - rankStart));
    }
}
