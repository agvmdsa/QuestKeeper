using System.Globalization;

namespace AqwCompanion.Skua;

/// <summary>
/// The pure rule the plugin follows to report the factions it read from Skua's <c>Reputation.FactionList</c>, kept here
/// (the plugin compiles this library in as source) so it is tested without Skua.
/// </summary>
public static class SkuaFactionRead
{
    /// <summary>
    /// The list to report after a read. <paramref name="read"/> null means the read failed or Skua answered nothing: the
    /// last list stays (null when there never was one, which reports "no reputation known"). Otherwise the factions as
    /// read: Skua's order, entries with a negative id or an id seen before left out, names trimmed (a blank name becomes
    /// <c>#id</c>), a negative number sent as 0 (the receiver drops an entry with a negative number), at most
    /// <see cref="SkuaProtocol.MaxFactions"/>.
    /// </summary>
    public static IReadOnlyList<SkuaFaction>? Next(
        IReadOnlyList<SkuaFaction>? last, IEnumerable<(int Id, string? Name, int Rank, int TotalRep, int Rep, int RequiredRep)>? read)
    {
        if (read is null)
        {
            return last;
        }

        var result = new List<SkuaFaction>();
        var seen = new HashSet<int>();
        foreach (var (id, name, rank, totalRep, rep, requiredRep) in read)
        {
            if (result.Count >= SkuaProtocol.MaxFactions)
            {
                break;
            }

            if (id < 0 || !seen.Add(id))
            {
                continue;
            }

            var shown = string.IsNullOrWhiteSpace(name) ? $"#{id.ToString(CultureInfo.InvariantCulture)}" : name.Trim();
            result.Add(new SkuaFaction(id, shown, Math.Max(0, rank), Math.Max(0, totalRep), Math.Max(0, rep), Math.Max(0, requiredRep)));
        }

        return result;
    }
}
