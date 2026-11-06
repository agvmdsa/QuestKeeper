using AqwCompanion.App.SignIn;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Wiki;
using Serilog;

namespace AqwCompanion.App.HardFarms;

/// <summary>Where a pass is: <see cref="Done"/> of <see cref="Total"/> index pages finished, the page being read, and the wait before a retry (null when not waiting).</summary>
public sealed record SyncProgress(int Done, int Total, string Label, string? Waiting);

public enum SyncStatus
{
    /// <summary>Everything needed was read and stored.</summary>
    Updated,

    /// <summary>Something was stored, but part of the pass was skipped, rejected or stopped; see the message and <see cref="SyncResult.Pending"/>.</summary>
    Partial,

    /// <summary>Nothing new was stored.</summary>
    Failed,

    /// <summary>The user cancelled; whatever was already saved stays.</summary>
    Cancelled,
}

/// <summary>What a pass did. <see cref="Pending"/> are the index pages (slugs) still unread or older than the allowed age.</summary>
public sealed record SyncResult(SyncStatus Status, string Message, IReadOnlyList<string> Pending);

/// <summary>
/// Told after every save: <see cref="List"/> is set only when the list itself was stored; <see cref="Types"/> is the type table
/// rebuilt from every index stored so far (with <see cref="TypesReadAtUtc"/>, the newest index read time).
/// </summary>
public sealed record SyncUpdate(HardFarmList? List, DateTimeOffset? ListReadAtUtc, WikiTypeTable Types, DateTimeOffset? TypesReadAtUtc);

/// <summary>The numbers of a pass; the defaults are the agreed ones, tests inject their own.</summary>
public sealed record HardFarmSyncOptions
{
    /// <summary>The pause before every request but the first.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>An answer that took longer than this doubles the next pause.</summary>
    public TimeSpan SlowAnswer { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>The longest pause after doubling.</summary>
    public TimeSpan MaxPause { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>The wait before the second, third... attempt of a page (the last one repeats).</summary>
    public IReadOnlyList<TimeSpan> RetryWaits { get; init; } = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public int MaxAttempts { get; init; } = 3;

    /// <summary>This many failed requests in a row (any pages) stop the pass.</summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    /// <summary>An index read more recently than this is not read again.</summary>
    public TimeSpan IndexMaxAge { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan ListTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan IndexTimeout { get; init; } = TimeSpan.FromSeconds(60);
}

public interface IHardFarmSync
{
    /// <summary>The type index pages (slugs, in fetch order) that were never read or were read longer ago than the allowed age. Never throws.</summary>
    IReadOnlyList<string> GetPending();

    /// <summary>
    /// Runs one pass: robots.txt, the list, then every pending type index, one request at a time. Each success is saved at once and
    /// reported through <paramref name="saved"/>. Never throws; cancellation is reported as <see cref="SyncStatus.Cancelled"/>.
    /// </summary>
    Task<SyncResult> RunAsync(Action<SyncProgress>? progress, Action<SyncUpdate>? saved, CancellationToken ct);
}

/// <summary>
/// The user-triggered import as a queue (see the AQW-0019 design): robots.txt, /syfarm, then one job per type index that is not
/// recent. A failed page goes to the back of the queue and is retried after a growing wait; the wiki refusing us, or several
/// failures in a row, stop the pass. Everything read is stored page by page, so a stopped or cancelled pass loses nothing.
/// </summary>
public sealed class HardFarmSync : IHardFarmSync
{
    private const string NotAnsweringWell = "The Wiki is not answering well; try again later.";
    private const string ListName = "the Hard Farming list";

    private readonly IWikiPageFetcher _fetcher;
    private readonly IHardFarmStore _store;
    private readonly IDelay _delay;
    private readonly TimeProvider _clock;
    private readonly HardFarmSyncOptions _options;
    private readonly HardFarmListParser _listParser = new();
    private readonly WikiIndexParser _indexParser = new();

    public HardFarmSync(
        IWikiPageFetcher fetcher,
        IHardFarmStore store,
        IDelay delay,
        TimeProvider clock,
        HardFarmSyncOptions? options = null)
    {
        _fetcher = fetcher;
        _store = store;
        _delay = delay;
        _clock = clock;
        _options = options ?? new HardFarmSyncOptions();
    }

    public IReadOnlyList<string> GetPending()
    {
        try
        {
            return PendingFrom(_store.LoadIndexes().ToDictionary(i => i.Index.IndexSlug, i => i.ReadAtUtc), _clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Reading the pending Wiki pages failed: {ExceptionType}", ex.GetType().Name);
            return [.. WikiUrls.TypeIndexSlugs];
        }
    }

    public async Task<SyncResult> RunAsync(Action<SyncProgress>? progress, Action<SyncUpdate>? saved, CancellationToken ct)
    {
        var pass = new Pass(this, progress, saved, ct);
        try
        {
            return await pass.RunAsync();
        }
        catch (OperationCanceledException)
        {
            return pass.Finish(SyncStatus.Cancelled, "Update cancelled. Everything read so far was kept.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("The Hard Farms update failed unexpectedly: {ExceptionType}", ex.GetType().Name);
            return pass.Finish(SyncStatus.Failed, "The update failed unexpectedly. Whatever was read before it is kept.");
        }
    }

    private bool IsFresh(DateTimeOffset readAt, DateTimeOffset now)
    {
        var age = now - readAt;
        return age < _options.IndexMaxAge && age > -TimeSpan.FromDays(1);
    }

    private IReadOnlyList<string> PendingFrom(IReadOnlyDictionary<string, DateTimeOffset> readAt, DateTimeOffset now) =>
        WikiUrls.TypeIndexSlugs.Where(slug => !(readAt.TryGetValue(slug, out var at) && IsFresh(at, now))).ToList();

    private static string PathOf(string slug) => new Uri(WikiUrls.Page(slug)).PathAndQuery;

    private static string PageName(string slug) => slug == WikiUrls.HardFarmSlug ? ListName : "/" + slug;

    private static string Describe(string page, PageFetchResult result) => result.Kind switch
    {
        PageFailureKind.Timeout => $"Could not read {page}: it took too long to answer.",
        PageFailureKind.Network => $"Could not read {page}: the connection failed.",
        PageFailureKind.Http => $"Could not read {page}: the Wiki answered HTTP {result.HttpStatus}.",
        PageFailureKind.Blocked => $"Could not read {page}: the request was not allowed.",
        PageFailureKind.TooLarge => $"Could not read {page}: the page is larger than allowed.",
        PageFailureKind.NotHtml => $"Could not read {page}: the answer was not a web page.",
        _ => $"Could not read {page}: the connection failed.",
    };

    private static string Seconds(TimeSpan span) => ((int)Math.Ceiling(span.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);

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

    private sealed record Job(string Slug, int Attempts)
    {
        public bool IsList => Slug == WikiUrls.HardFarmSlug;
    }

    /// <summary>The state of one pass.</summary>
    private sealed class Pass(HardFarmSync owner, Action<SyncProgress>? progress, Action<SyncUpdate>? saved, CancellationToken ct)
    {
        private readonly Dictionary<string, DateTimeOffset> _readAt = new(StringComparer.Ordinal);
        private readonly List<WikiIndex> _indexes = [];
        private readonly List<string> _problems = [];
        private readonly List<string> _warnings = [];
        private int _done;
        private int _consecutiveFailures;
        private TimeSpan _lastAnswer;
        private bool _firstRequestSent;
        private bool _savedAnything;
        private int _indexesRead;
        private int _listCount = -1;
        private string? _stopped;

        private HardFarmSyncOptions Options => owner._options;

        public async Task<SyncResult> RunAsync()
        {
            ct.ThrowIfCancellationRequested();
            foreach (var stored in await Task.Run(() => owner._store.LoadIndexes(), CancellationToken.None))
            {
                _readAt[stored.Index.IndexSlug] = stored.ReadAtUtc;
                _indexes.Add(stored.Index);
            }

            var needed = owner.PendingFrom(_readAt, owner._clock.GetUtcNow());
            _done = WikiUrls.TypeIndexSlugs.Count - needed.Count;

            // 1. robots.txt, and every path this pass needs is checked before the first page is requested.
            Report("robots.txt", null);
            var robotsResult = await TimedAsync(() => owner._fetcher.FetchRobotsAsync(ct));
            if (!robotsResult.IsSuccess)
            {
                return Finish(SyncStatus.Failed, "Could not read the Wiki's robots.txt. The update was stopped and nothing was changed.");
            }

            var robots = RobotsTxt.Parse(robotsResult.Snapshot!.Payload);
            foreach (var slug in needed.Prepend(WikiUrls.HardFarmSlug))
            {
                if (!robots.IsAllowed(PathOf(slug)))
                {
                    return Finish(SyncStatus.Failed, $"The Wiki's robots.txt does not allow reading /{slug}. The update was stopped and nothing was changed.");
                }
            }

            // 2. and 3. The queue: the list first, then the indexes in their order.
            var queue = new Queue<Job>(needed.Select(slug => new Job(slug, 0)).Prepend(new Job(WikiUrls.HardFarmSlug, 0)));
            while (queue.Count > 0 && _stopped is null)
            {
                ct.ThrowIfCancellationRequested();
                var job = queue.Dequeue();
                await ProcessAsync(job, queue);
            }

            if (_stopped is not null)
            {
                return Finish(_savedAnything ? SyncStatus.Partial : SyncStatus.Failed, _stopped);
            }

            var pending = owner.PendingFrom(_readAt, owner._clock.GetUtcNow());
            var clean = _problems.Count == 0 && _warnings.Count == 0 && pending.Count == 0;
            return Finish(clean ? SyncStatus.Updated : _savedAnything ? SyncStatus.Partial : SyncStatus.Failed, null);
        }

        public SyncResult Finish(SyncStatus status, string? headline)
        {
            var pending = owner.PendingFrom(_readAt, owner._clock.GetUtcNow());
            var parts = new List<string>();
            if (headline is not null)
            {
                parts.Add(headline);
            }

            if (status != SyncStatus.Failed || _savedAnything)
            {
                var stored = new List<string>();
                if (_listCount >= 0)
                {
                    stored.Add($"{_listCount} entries in the list");
                }

                if (_indexesRead > 0)
                {
                    stored.Add($"{_indexesRead} index {(_indexesRead == 1 ? "page" : "pages")}");
                }

                if (stored.Count > 0)
                {
                    parts.Add("Stored " + string.Join(" and ", stored) + ".");
                }
                else if (status == SyncStatus.Updated)
                {
                    parts.Add("Everything was already up to date.");
                }
            }

            parts.AddRange(_problems);
            parts.AddRange(_warnings);
            if (pending.Count > 0 && status is SyncStatus.Partial or SyncStatus.Cancelled)
            {
                parts.Add($"{pending.Count} {(pending.Count == 1 ? "page" : "pages")} pending.");
            }

            return new SyncResult(status, string.Join(' ', parts), pending);
        }

        private async Task ProcessAsync(Job job, Queue<Job> queue)
        {
            var page = PageName(job.Slug);
            var label = job.IsList
                ? "Hard Farming list"
                : $"Index {WikiUrls.TypeIndexSlugs.ToList().IndexOf(job.Slug) + 1} of {WikiUrls.TypeIndexSlugs.Count} · /{job.Slug}";

            var wait = Pause();
            string? waiting = null;
            if (job.Attempts > 0)
            {
                var waits = Options.RetryWaits;
                var retryWait = waits.Count == 0 ? TimeSpan.Zero : waits[Math.Min(job.Attempts - 1, waits.Count - 1)];
                if (retryWait > wait)
                {
                    wait = retryWait;
                }

                waiting = $"Waiting {Seconds(wait)} s before trying {page} again";
            }

            Report(label, waiting);
            await owner._delay.DelayAsync(wait, ct);
            if (waiting is not null)
            {
                Report(label, null);
            }

            var timeout = job.IsList ? Options.ListTimeout : Options.IndexTimeout;
            var result = await TimedAsync(() => owner._fetcher.FetchPageAsync(WikiUrls.Page(job.Slug), timeout, ct));
            ct.ThrowIfCancellationRequested();

            if (result.IsSuccess)
            {
                _consecutiveFailures = 0;
                await HandlePageAsync(job, page, result.Snapshot!);
                return;
            }

            var message = Describe(page, result);
            var blocked = result.Kind == PageFailureKind.Blocked
                || (result.Kind == PageFailureKind.Http && result.HttpStatus is 403 or 429);
            if (blocked)
            {
                _stopped = $"{message} {NotAnsweringWell}";
                return;
            }

            // Every failed request counts toward stopping the pass, retried or not; a success resets the count.
            var retriable = result.Kind is PageFailureKind.Timeout or PageFailureKind.Network or PageFailureKind.None
                || (result.Kind == PageFailureKind.Http && result.HttpStatus >= 500);
            if (++_consecutiveFailures >= Options.MaxConsecutiveFailures)
            {
                _stopped = $"{message} {NotAnsweringWell}";
                return;
            }

            if (!retriable)
            {
                _problems.Add(message);
                Finished(job);
                return;
            }

            if (job.Attempts + 1 < Options.MaxAttempts)
            {
                queue.Enqueue(job with { Attempts = job.Attempts + 1 });
            }
            else
            {
                _problems.Add(message);
                Finished(job);
            }
        }

        private async Task HandlePageAsync(Job job, string page, PageSnapshot snapshot)
        {
            if (job.IsList)
            {
                await HandleListAsync(snapshot);
                return;
            }

            var parsed = await ParseAsync(owner._indexParser, snapshot, ct);
            if (parsed is { Outcome: ParseOutcome.Ok, Data: { } index })
            {
                ct.ThrowIfCancellationRequested();
                var now = owner._clock.GetUtcNow();
                await Task.Run(() => owner._store.SaveIndex(index, now), CancellationToken.None);
                _readAt[job.Slug] = now;
                var existing = _indexes.FindIndex(i => i.IndexSlug == index.IndexSlug);
                if (existing >= 0)
                {
                    _indexes[existing] = index;
                }
                else
                {
                    _indexes.Add(index);
                }

                _savedAnything = true;
                _indexesRead++;
                Finished(job);
                Notify(null, null);
            }
            else if (job.Slug == WikiUrls.ClassesSlug)
            {
                // Nothing can be typed without the class index, so the other pages would be requested for nothing.
                _stopped = "The class index could not be understood (the page layout may have changed), so the update was stopped. What was saved is kept.";
            }
            else
            {
                _problems.Add($"Could not understand {page} (the page layout may have changed), so it was skipped.");
                Finished(job);
            }
        }

        private async Task HandleListAsync(PageSnapshot snapshot)
        {
            var parsed = await ParseAsync(owner._listParser, snapshot, ct);
            switch (parsed?.Outcome)
            {
                case ParseOutcome.Ok when parsed.Data is { } list:
                    // The same drop guard the account pages go through: a list far shorter than the stored one is not trusted.
                    var previous = await Task.Run(() => owner._store.LoadList()?.List.Count, CancellationToken.None);
                    if (previous is >= IngestionService<HardFarmList>.DropMinPrevious
                        && list.Count < previous.Value * IngestionService<HardFarmList>.DropThreshold)
                    {
                        _warnings.Add(
                            $"The Hard Farming list shrank from {previous.Value} to {list.Count} entries (the page may have been damaged), so the previous list was kept.");
                        return;
                    }

                    ct.ThrowIfCancellationRequested();
                    var now = owner._clock.GetUtcNow();
                    await Task.Run(() => owner._store.SaveList(list, now), CancellationToken.None);
                    _savedAnything = true;
                    _listCount = list.Count;
                    Notify(list, now);
                    break;
                case ParseOutcome.Suspicious:
                    _warnings.Add("The Hard Farming list looked wrong (the page layout may have changed), so the previous list was kept.");
                    break;
                default:
                    _warnings.Add("The Hard Farming list could not be understood (the page layout may have changed), so the previous list was kept.");
                    break;
            }
        }

        private void Finished(Job job)
        {
            if (!job.IsList)
            {
                _done++;
            }
        }

        private TimeSpan Pause()
        {
            if (!_firstRequestSent)
            {
                return Options.Pause;
            }

            if (_lastAnswer > Options.SlowAnswer)
            {
                var doubled = Options.Pause + Options.Pause;
                return doubled < Options.MaxPause ? doubled : Options.MaxPause;
            }

            return Options.Pause;
        }

        private async Task<PageFetchResult> TimedAsync(Func<Task<PageFetchResult>> request)
        {
            var started = owner._clock.GetTimestamp();
            try
            {
                return await request();
            }
            finally
            {
                _lastAnswer = owner._clock.GetElapsedTime(started);
                _firstRequestSent = true;
            }
        }

        private void Report(string label, string? waiting)
        {
            try
            {
                progress?.Invoke(new SyncProgress(_done, WikiUrls.TypeIndexSlugs.Count, label, waiting));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("A progress handler failed: {ExceptionType}", ex.GetType().Name);
            }
        }

        private void Notify(HardFarmList? list, DateTimeOffset? listReadAt)
        {
            try
            {
                saved?.Invoke(new SyncUpdate(
                    list,
                    listReadAt,
                    WikiTypeTable.From(_indexes),
                    _readAt.Count == 0 ? null : _readAt.Values.Max()));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("A saved-data handler failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
