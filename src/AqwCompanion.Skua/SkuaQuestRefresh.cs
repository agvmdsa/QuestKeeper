using System.Globalization;

namespace AqwCompanion.Skua;

/// <summary>
/// The pure rules the plugin follows to report active quests, kept here (the plugin compiles this library in as source)
/// so they are tested without Skua. Reading whether a quest can be turned in costs a game read per quest, so those
/// flags are re-read only when the set of active quests changed or <see cref="ReadyInterval"/> has passed.
/// </summary>
public static class SkuaQuestRefresh
{
    public static readonly TimeSpan ReadyInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The active quests as they will be reported: entries with a negative id or an id seen before are left out, names are
    /// trimmed (a blank name becomes <c>#id</c>), and at most <see cref="SkuaProtocol.MaxQuests"/> are kept, in Skua's order.
    /// </summary>
    public static IReadOnlyList<(int Id, string Name)> Active(IEnumerable<(int Id, string? Name)> quests)
    {
        var result = new List<(int Id, string Name)>();
        var seen = new HashSet<int>();
        foreach (var (id, name) in quests)
        {
            if (result.Count >= SkuaProtocol.MaxQuests)
            {
                break;
            }

            if (id < 0 || !seen.Add(id))
            {
                continue;
            }

            result.Add((id, string.IsNullOrWhiteSpace(name) ? $"#{id.ToString(CultureInfo.InvariantCulture)}" : name.Trim()));
        }

        return result;
    }

    /// <summary>
    /// Whether the ready flags must be read now: never read yet (<paramref name="lastIds"/> null), the set of active quest
    /// ids differs from the one the flags were read for (order does not matter), or <see cref="ReadyInterval"/> has passed.
    /// </summary>
    public static bool ReadyDue(IReadOnlyCollection<int>? lastIds, IReadOnlyCollection<int> ids, TimeSpan sinceLastRead) =>
        lastIds is null || sinceLastRead >= ReadyInterval || !lastIds.ToHashSet().SetEquals(ids);

    /// <summary>The reported list: each active quest with its last read flag (a quest with no flag yet is not ready).</summary>
    public static IReadOnlyList<SkuaQuest> Build(IReadOnlyList<(int Id, string Name)> active, IReadOnlyDictionary<int, bool> ready) =>
        active.Select(q => new SkuaQuest(q.Id, q.Name, ready.TryGetValue(q.Id, out var r) && r)).ToList();
}
