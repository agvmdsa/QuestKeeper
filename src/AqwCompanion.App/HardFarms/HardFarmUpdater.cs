using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Wiki;
using Serilog;

namespace AqwCompanion.App.HardFarms;

/// <summary>"Step n of N" with the page being read.</summary>
public sealed record HardFarmProgress(int Step, int Total, string Label)
{
    public string Text => $"Step {Step} of {Total}: {Label}";
}

public enum HardFarmUpdateStatus
{
    /// <summary>Everything was read and stored.</summary>
    Updated,

    /// <summary>Something new was stored, but part of the update was skipped or rejected; see the message.</summary>
    Partial,

    /// <summary>Nothing was changed.</summary>
    Failed,

    Cancelled,
}

/// <summary>What an update did. <see cref="List"/> and <see cref="Types"/> are set only when they were stored (and replace the previous ones).</summary>
public sealed record HardFarmUpdateResult(
    HardFarmUpdateStatus Status,
    string Message,
    HardFarmList? List = null,
    WikiTypeTable? Types = null,
    DateTimeOffset? ReadAtUtc = null);

public interface IHardFarmUpdater
{
    /// <summary>Runs one update. Never throws; cancellation is reported as <see cref="HardFarmUpdateStatus.Cancelled"/>.</summary>
    Task<HardFarmUpdateResult> UpdateAsync(Action<HardFarmProgress>? progress, CancellationToken ct);
}

/// <summary>
/// The user-triggered import: robots.txt, then /syfarm, then the type indexes, strictly one after the other with a pause between
/// requests. It stops at the first error and keeps the previous stored data for whatever it did not replace.
/// </summary>
public sealed class HardFarmUpdater(
    IWikiPageFetcher fetcher,
    IHardFarmStore store,
    IDelay delay,
    TimeProvider clock,
    TimeSpan? pacing = null) : IHardFarmUpdater
{
    public static readonly TimeSpan DefaultPacing = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _pacing = pacing ?? DefaultPacing;
    private readonly HardFarmListParser _listParser = new();
    private readonly WikiIndexParser _indexParser = new();

    /// <summary>Steps of an update: robots.txt, /syfarm and the type indexes.</summary>
    public static int TotalSteps => 2 + WikiUrls.TypeIndexSlugs.Count;

    public async Task<HardFarmUpdateResult> UpdateAsync(Action<HardFarmProgress>? progress, CancellationToken ct)
    {
        try
        {
            return await RunAsync(progress, ct);
        }
        catch (OperationCanceledException)
        {
            return new HardFarmUpdateResult(HardFarmUpdateStatus.Cancelled, "Update cancelled. Nothing was changed.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("The Hard Farms update failed unexpectedly: {ExceptionType}", ex.GetType().Name);
            return Failed("The update failed unexpectedly. Nothing was changed.");
        }
    }

    private async Task<HardFarmUpdateResult> RunAsync(Action<HardFarmProgress>? progress, CancellationToken ct)
    {
        var step = 0;

        // 1. robots.txt, and every path this update needs is checked before anything else is requested.
        Report(progress, ++step, "robots.txt");
        var robotsResult = await fetcher.FetchRobotsAsync(ct);
        if (!robotsResult.IsSuccess)
        {
            return Failed($"Could not read the Wiki's robots.txt ({robotsResult.Error}). The update was stopped and nothing was changed.");
        }

        var robots = RobotsTxt.Parse(robotsResult.Snapshot!.Payload);
        foreach (var slug in WikiUrls.TypeIndexSlugs.Prepend(WikiUrls.HardFarmSlug))
        {
            if (!robots.IsAllowed(PathOf(slug)))
            {
                return Failed($"The Wiki's robots.txt does not allow reading /{slug}. The update was stopped and nothing was changed.");
            }
        }

        // 2. The list itself.
        await delay.DelayAsync(_pacing, ct);
        Report(progress, ++step, "Hard Farming list");
        var listPage = await fetcher.FetchPageAsync(WikiUrls.HardFarm, ct);
        if (!listPage.IsSuccess)
        {
            return Failed($"Could not read the Hard Farming list ({listPage.Error}). The update was stopped and nothing was changed.");
        }

        var parsed = await ParseAsync(_listParser, listPage.Snapshot!, ct);
        HardFarmList? newList = null;
        var warnings = new List<string>();
        switch (parsed?.Outcome)
        {
            case ParseOutcome.Ok when parsed.Data is { } okList:
                // The same drop guard the account pages go through: a list far shorter than the stored one is not trusted.
                var previous = await Task.Run(() => store.LoadList()?.List.Count, CancellationToken.None);
                if (previous is >= IngestionService<HardFarmList>.DropMinPrevious
                    && okList.Count < previous.Value * IngestionService<HardFarmList>.DropThreshold)
                {
                    warnings.Add(
                        $"The Hard Farming list shrank from {previous.Value} to {okList.Count} entries (the page may have been damaged), so the previous list was kept.");
                }
                else
                {
                    newList = okList;
                }

                break;
            case ParseOutcome.Suspicious:
                warnings.Add("The Hard Farming list looked wrong (the page layout may have changed), so the previous list was kept.");
                break;
            default:
                return Failed("The Hard Farming list could not be understood (the page layout may have changed). The update was stopped and nothing was changed.");
        }

        var now = clock.GetUtcNow();
        if (newList is not null)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Run(() => store.SaveList(newList, now), CancellationToken.None);
        }

        // 3. The type indexes.
        var indexes = new List<WikiIndex>();
        var skipped = new List<string>();
        string? stoppedBecause = null;
        var cancelled = false;
        try
        {
            foreach (var slug in WikiUrls.TypeIndexSlugs)
            {
                await delay.DelayAsync(_pacing, ct);
                Report(progress, ++step, $"/{slug}");
                var page = await fetcher.FetchPageAsync(WikiUrls.Page(slug), ct);
                if (!page.IsSuccess)
                {
                    stoppedBecause = $"Could not read /{slug} ({page.Error}). The update was stopped.";
                    break;
                }

                var index = await ParseAsync(_indexParser, page.Snapshot!, ct);
                if (index is { Outcome: ParseOutcome.Ok, Data: { } data })
                {
                    indexes.Add(data);
                }
                else if (slug == WikiUrls.ClassesSlug)
                {
                    // Nothing can be stored without the class index, so the other pages would be requested for nothing.
                    stoppedBecause = "The class index could not be understood (the page layout may have changed), so the update was stopped.";
                    break;
                }
                else
                {
                    skipped.Add(slug);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        if (stoppedBecause is not null || cancelled)
        {
            var read = indexes.Select(i => i.IndexSlug).ToHashSet(StringComparer.Ordinal);
            skipped = WikiUrls.TypeIndexSlugs.Where(s => !read.Contains(s)).ToList();
        }

        WikiTypeTable? newTypes = null;
        if (cancelled || ct.IsCancellationRequested)
        {
            // A cancelled run (Sign out may have removed the stored data meanwhile) writes nothing more.
            cancelled = true;
        }
        else if (indexes.Any(i => i.IndexSlug == WikiUrls.ClassesSlug))
        {
            newTypes = WikiTypeTable.From(indexes);
            await Task.Run(() => store.SaveTypes(newTypes, now), CancellationToken.None);
        }
        else
        {
            warnings.Add("The class index could not be read, so the previous item types were kept.");
        }

        if (skipped.Count > 0 && newTypes is not null)
        {
            warnings.Add($"Skipped index pages: {string.Join(", ", skipped.Select(s => "/" + s))}. Items found only there stay as Other.");
        }

        if (cancelled)
        {
            return new HardFarmUpdateResult(
                HardFarmUpdateStatus.Cancelled,
                "Update cancelled. " + Summary(newList, newTypes) + (warnings.Count > 0 ? " " + string.Join(" ", warnings) : string.Empty),
                newList,
                newTypes,
                now);
        }

        if (stoppedBecause is not null)
        {
            return new HardFarmUpdateResult(
                newList is null && newTypes is null ? HardFarmUpdateStatus.Failed : HardFarmUpdateStatus.Partial,
                stoppedBecause + " " + Summary(newList, newTypes) + " " + string.Join(" ", warnings),
                newList,
                newTypes,
                now);
        }

        return new HardFarmUpdateResult(
            warnings.Count == 0 ? HardFarmUpdateStatus.Updated : HardFarmUpdateStatus.Partial,
            (Summary(newList, newTypes) + " " + string.Join(" ", warnings)).Trim(),
            newList,
            newTypes,
            now);
    }

    private static string Summary(HardFarmList? list, WikiTypeTable? types)
    {
        var parts = new List<string>();
        if (list is not null)
        {
            parts.Add($"{list.Count} entries in {list.Groups.Count} groups");
        }

        if (types is not null)
        {
            parts.Add($"{types.Count} item names");
        }

        return parts.Count == 0 ? "Nothing new was stored." : "Stored " + string.Join(" and ", parts) + ".";
    }

    private static async Task<ParseResult<T>?> ParseAsync<T>(IPageParser<T> parser, PageSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            return parser.CanParse(snapshot) ? await Task.Run(() => parser.Parse(snapshot), ct) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            Log.Error("A wiki page could not be parsed: {ExceptionType}", ex.GetType().Name);
            return null;
        }
    }

    private static void Report(Action<HardFarmProgress>? progress, int step, string label) =>
        progress?.Invoke(new HardFarmProgress(step, TotalSteps, label));

    private static string PathOf(string slug) => new Uri(WikiUrls.Page(slug)).PathAndQuery;

    private static HardFarmUpdateResult Failed(string message) => new(HardFarmUpdateStatus.Failed, message);
}
