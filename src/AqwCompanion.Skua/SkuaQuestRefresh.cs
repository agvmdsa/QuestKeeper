using System.Globalization;

namespace AqwCompanion.Skua;

/// <summary>
/// The pure rules the plugin follows to report active quests, kept here (the plugin compiles this library in as source)
/// so they are tested without Skua. Every read of Skua's <c>Quests.Active</c> rebuilds Skua's own shared quest-tree cache,
/// so the list is read at most every <see cref="ReadInterval"/> (or at once for a forced reason such as a new connection or
/// a login), and the last list is reported in between.
/// </summary>
public static class SkuaQuestRefresh
{
    public static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(15);

    /// <summary>Skua's quest status for "can be turned in": its own <c>CanComplete</c> is <c>Completed</c>, the quests whose status is "c".</summary>
    public const string CompletedStatus = "c";

    /// <summary>Whether <c>Quests.Active</c> is read now: never read yet, a forced reason (connect, login), or <see cref="ReadInterval"/> has passed.</summary>
    public static bool ReadDue(bool everRead, bool forced, TimeSpan sinceLastRead) =>
        !everRead || forced || sinceLastRead >= ReadInterval;

    /// <summary>
    /// The list to report after a read. <paramref name="read"/> null means the read failed or Skua answered nothing: the
    /// last list stays (null when there never was one, which reports "no quests known"). Otherwise the quests as reported:
    /// Skua's order, entries with a negative id or an id seen before left out, names trimmed (a blank name becomes
    /// <c>#id</c>), ready when the status is <see cref="CompletedStatus"/>, at most <see cref="SkuaProtocol.MaxQuests"/>.
    /// </summary>
    public static IReadOnlyList<SkuaQuest>? Next(IReadOnlyList<SkuaQuest>? last, IEnumerable<(int Id, string? Name, string? Status)>? read)
    {
        if (read is null)
        {
            return last;
        }

        var result = new List<SkuaQuest>();
        var seen = new HashSet<int>();
        foreach (var (id, name, status) in read)
        {
            if (result.Count >= SkuaProtocol.MaxQuests)
            {
                break;
            }

            if (id < 0 || !seen.Add(id))
            {
                continue;
            }

            var shown = string.IsNullOrWhiteSpace(name) ? $"#{id.ToString(CultureInfo.InvariantCulture)}" : name.Trim();
            result.Add(new SkuaQuest(id, shown, status == CompletedStatus));
        }

        return result;
    }
}
