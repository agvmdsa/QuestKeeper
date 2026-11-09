using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Feed;

namespace AqwCompanion.App.Feed;

/// <summary>Turns the capture session, the selected character and Hard Farms into the snapshot the local feed serves.</summary>
public static class FeedSnapshotBuilder
{
    public static FeedSnapshot Build(CaptureSession session, GameDataViewModel gameData, HardFarmsViewModel hardFarms)
    {
        var snapshots = session.Characters.OrderBy(c => c.Profile.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // The session keeps a single "last kill"; it belongs to the live character that was updated last.
        var live = session.Live;
        var latest = snapshots
            .Select(c => (Name: c.Profile.Name, At: session.LastLiveUpdateUtc(c.Profile.Name)))
            .Where(x => x.At is not null)
            .OrderByDescending(x => x.At)
            .Select(x => x.Name)
            .FirstOrDefault();
        var kill = live.LastGoldGain == 0 && live.LastExpGain == 0 ? null : new FeedKill(live.LastGoldGain, live.LastExpGain);

        var characters = snapshots.Select(c =>
        {
            var p = c.Profile;
            return new FeedCharacter(
                p.Name,
                p.Level,
                session.IsLive(p.Name),
                c.CapturedAtUtc,
                session.LastLiveUpdateUtc(p.Name),
                session.LiveUpdateCount(p.Name),
                new FeedFigures(p.Gold, p.Exp, p.ExpToLevel, p.Coins, p.ClassPoints, p.DaysPlayed),
                string.Equals(p.Name, latest, StringComparison.Ordinal) ? kill : null);
        }).ToList();

        var factions = gameData.ActiveSnapshot?.Factions
            .OrderBy(FactionProgress.Missing)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FeedFaction(
                f.Name,
                f.Rank,
                f.Rep,
                FactionProgress.IsMax(f) ? 0 : Math.Max(0, (long)f.RepToRank - f.SpillRep),
                FactionProgress.Fraction(f),
                FactionProgress.IsMax(f)))
            .ToList() ?? [];

        var summary = gameData.ActiveSnapshot is null ? null : hardFarms.Summary;
        return new FeedSnapshot(
            FeedSnapshot.ContractNumber,
            new FeedCapture(gameData.StateText, gameData.IsListening),
            gameData.SelectedCharacter,
            characters,
            factions,
            summary is null ? null : new FeedHardFarms(summary.Owned, summary.Total, FeedHardFarms.Cap(summary.Missing)));
    }
}
