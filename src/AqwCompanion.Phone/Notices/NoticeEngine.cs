using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.Phone.Notices;

/// <summary>Turns the stream of snapshots (and the passing of time) into notices. Pure: no clock, no I/O; the caller passes <c>now</c>. Not thread-safe: the caller (the hub) serialises <c>Process</c>, <c>Tick</c> and <c>Reset</c>.</summary>
public sealed class NoticeEngine(NoticeOptions? options = null)
{
    private const int RememberedIds = 512;

    private readonly NoticeOptions _options = options ?? NoticeOptions.Default;
    private readonly HashSet<string> _seen = [];
    private readonly Queue<string> _seenOrder = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.Ordinal);
    private FeedSnapshot? _previous;
    private Dictionary<string, bool>? _skuaConnected;

    /// <summary>Forgets everything: the next snapshot is a baseline again and no notice id is remembered.</summary>
    public void Reset()
    {
        _previous = null;
        _skuaConnected = null;
        _seen.Clear();
        _seenOrder.Clear();
        _recent.Clear();
    }

    public IReadOnlyList<Notice> Process(FeedSnapshot snapshot, DateTimeOffset now)
    {
        var found = new List<Notice>();
        if (_previous is { } before)
        {
            Capture(before, snapshot, now, found);
            Logins(before, snapshot, now, found);
            Ranks(before, snapshot, now, found);
            HardFarmDrops(before, snapshot, now, found);
        }

        _previous = snapshot;
        Stalls(snapshot, now, found);
        return Admit(found, now);
    }

    /// <summary>
    /// The Skua instances changed: a connected Skua that is now disconnected raises <see cref="NoticeKind.SkuaDisconnected"/>,
    /// a disconnected one that is connected again <see cref="NoticeKind.SkuaReconnected"/> (the instance label in the text).
    /// The first view is a baseline; an instance that appears or goes away raises nothing.
    /// </summary>
    public IReadOnlyList<Notice> ProcessSkua(SkuaHubView view, DateTimeOffset now)
    {
        var found = new List<Notice>();
        var connected = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var instance in view.Instances)
        {
            connected[instance.InstanceId] = !instance.Disconnected;
            if (_skuaConnected is null || !_skuaConnected.TryGetValue(instance.InstanceId, out var was) || was == !instance.Disconnected)
            {
                continue;
            }

            var label = instance.Label;
            found.Add(instance.Disconnected
                ? new Notice($"skua-off:{instance.InstanceId}:{now.UtcTicks}", NoticeKind.SkuaDisconnected, label, "Skua disconnected", $"{label} is no longer connected to SkuaLink.", now)
                : new Notice($"skua-on:{instance.InstanceId}:{now.UtcTicks}", NoticeKind.SkuaReconnected, label, "Skua reconnected", $"{label} is connected to SkuaLink again.", now));
        }

        _skuaConnected = connected;
        return Admit(found, now);
    }

    /// <summary>Time passing with no new snapshot: the feed is silent when nothing changes, so a stalled bot is found here.</summary>
    public IReadOnlyList<Notice> Tick(DateTimeOffset now)
    {
        if (_previous is null)
        {
            return [];
        }

        var found = new List<Notice>();
        Stalls(_previous, now, found);
        return Admit(found, now);
    }

    private static void Capture(FeedSnapshot before, FeedSnapshot now, DateTimeOffset at, List<Notice> found)
    {
        if (before.Capture.Listening == now.Capture.Listening)
        {
            return;
        }

        var who = now.Selected ?? "";
        found.Add(now.Capture.Listening
            ? new Notice($"capture-back:{at.UtcTicks}", NoticeKind.CaptureBack, who, "Capture running", "SkuaLink is listening to the game again.", at)
            : new Notice($"capture-lost:{at.UtcTicks}", NoticeKind.CaptureLost, who, "Capture stopped", "SkuaLink is no longer listening to the game.", at));
    }

    private static void Logins(FeedSnapshot before, FeedSnapshot now, DateTimeOffset at, List<Notice> found)
    {
        var known = before.Characters.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var c in now.Characters.Where(c => !known.Contains(c.Name)))
        {
            found.Add(new Notice($"login:{c.Name}:{at.UtcTicks}", NoticeKind.NewLogin, c.Name, "Character logged in", $"{c.Name} logged in.", at));
        }
    }

    private static void Ranks(FeedSnapshot before, FeedSnapshot now, DateTimeOffset at, List<Notice> found)
    {
        if (now.Selected is null || now.Selected != before.Selected)
        {
            return;
        }

        var old = before.Factions.ToDictionary(f => f.Name, f => f.Rank, StringComparer.Ordinal);
        foreach (var f in now.Factions.Where(f => old.TryGetValue(f.Name, out var rank) && f.Rank > rank))
        {
            found.Add(new Notice($"rank:{now.Selected}:{f.Name}:{f.Rank}", NoticeKind.RankUp, now.Selected, "Rank up", $"{f.Name} reached rank {f.Rank}.", at));
        }
    }

    private static void HardFarmDrops(FeedSnapshot before, FeedSnapshot now, DateTimeOffset at, List<Notice> found)
    {
        if (now.Selected is null || now.Selected != before.Selected
            || before.HardFarms is not { } old || now.HardFarms is not { } current
            || old.Total != current.Total || current.Owned <= old.Owned)
        {
            return;
        }

        var still = current.Missing.ToHashSet(StringComparer.Ordinal);
        foreach (var name in old.Missing.Where(n => !still.Contains(n)))
        {
            found.Add(new Notice($"hf:{now.Selected}:{name}", NoticeKind.HardFarmDrop, now.Selected, "Hard Farm obtained", $"{name} is now owned.", at));
        }
    }

    private void Stalls(FeedSnapshot snapshot, DateTimeOffset now, List<Notice> found)
    {
        var minutes = Math.Max(1, (int)_options.StallAfter.TotalMinutes);
        foreach (var c in snapshot.Characters.Where(c => c.Live && c.LastLiveUpdateUtc is { } last && now - last > _options.StallAfter))
        {
            found.Add(new Notice(
                $"stall:{c.Name}:{c.LastLiveUpdateUtc!.Value.UtcTicks}", NoticeKind.ActivityStopped, c.Name,
                "Activity stopped", $"{c.Name} has shown no activity for {minutes} minutes.", now));
        }
    }

    private List<Notice> Admit(List<Notice> found, DateTimeOffset now)
    {
        var admitted = new List<Notice>();
        foreach (var notice in found)
        {
            if (!_seen.Add(notice.Id))
            {
                continue;
            }

            _seenOrder.Enqueue(notice.Id);
            while (_seenOrder.Count > RememberedIds)
            {
                _seen.Remove(_seenOrder.Dequeue());
            }

            if (!_recent.TryGetValue(notice.Character, out var times))
            {
                _recent[notice.Character] = times = new Queue<DateTimeOffset>();
            }

            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(1))
            {
                times.Dequeue();
            }

            // The id was remembered above on purpose: a notice dropped by the rate limit is not reported later either.
            if (times.Count >= _options.MaxPerCharacterPerMinute)
            {
                continue;
            }

            times.Enqueue(now);
            admitted.Add(notice);
        }

        return admitted;
    }
}
