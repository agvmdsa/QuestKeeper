using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Serves canned pages by slug and records the order of requests (and of pauses, when <see cref="Log"/> is shared).</summary>
public sealed class FakeWikiFetcher : IWikiPageFetcher
{
    public List<string> Log { get; init; } = [];

    public Dictionary<string, string> Pages { get; } = [];

    public Dictionary<string, string> Errors { get; } = [];

    public string Robots { get; set; } = "User-agent: *\nDisallow: /system:\n";

    public string? RobotsError { get; set; }

    public Action<string>? OnFetch { get; set; }

    /// <summary>Scripted answers per slug, used one by one before <see cref="Pages"/> and <see cref="Errors"/> apply again.</summary>
    public Dictionary<string, Queue<PageFetchResult>> Scripts { get; } = [];

    /// <summary>The timeout every page request asked for, in order.</summary>
    public List<(string Slug, TimeSpan? Timeout)> Timeouts { get; } = [];

    /// <summary>Makes the next requests of <paramref name="slug"/> fail like this (once each), then serve the page again.</summary>
    public void Script(string slug, params PageFetchResult[] answers)
    {
        if (!Scripts.TryGetValue(slug, out var queue))
        {
            Scripts[slug] = queue = new Queue<PageFetchResult>();
        }

        foreach (var answer in answers)
        {
            queue.Enqueue(answer);
        }
    }

    public static PageFetchResult Timeout() => PageFetchResult.Failure("The request timed out.", PageFailureKind.Timeout);

    public static PageFetchResult Http(int status) => PageFetchResult.Failure($"The server answered HTTP {status}.", PageFailureKind.Http, status);

    public List<string> Requested => Log.Where(e => e.StartsWith("fetch:", StringComparison.Ordinal)).Select(e => e[6..]).ToList();

    public Task<PageFetchResult> FetchRobotsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Log.Add("fetch:robots.txt");
        return Task.FromResult(RobotsError is { } error
            ? PageFetchResult.Failure(error)
            : PageFetchResult.Success(PageSnapshot.Create(WikiUrls.Robots, Robots, "1.0.0", DateTimeOffset.UnixEpoch)));
    }

    public Task<PageFetchResult> FetchPageAsync(string url, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var slug = WikiUrls.Slug(url)!;
        Log.Add("fetch:" + slug);
        Timeouts.Add((slug, timeout));
        OnFetch?.Invoke(slug);
        if (Scripts.TryGetValue(slug, out var script) && script.Count > 0)
        {
            return Task.FromResult(script.Dequeue());
        }

        if (Errors.TryGetValue(slug, out var error))
        {
            return Task.FromResult(PageFetchResult.Failure(error));
        }

        return Task.FromResult(Pages.TryGetValue(slug, out var html)
            ? PageFetchResult.Success(PageSnapshot.Create(url, html, "1.0.0", DateTimeOffset.UnixEpoch))
            : PageFetchResult.Failure("HTTP 404", PageFailureKind.Http, 404));
    }
}

/// <summary>Writes "delay" into a shared log and completes at once, so no test waits real seconds.</summary>
public sealed class LoggingDelay(List<string> log) : IDelay
{
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Delays.Add(delay);
        log.Add("delay");
        return Task.CompletedTask;
    }
}

public sealed class FakeUrlLauncher : IUrlLauncher
{
    public List<string> Opened { get; } = [];

    public bool Open(string url)
    {
        Opened.Add(url);
        return true;
    }
}

/// <summary>A sync whose result the test controls; <see cref="Gate"/> holds it open to observe the Updating state.</summary>
public sealed class FakeHardFarmSync : IHardFarmSync
{
    public SyncResult Result { get; set; } = new(SyncStatus.Failed, "not set", []);

    public IReadOnlyList<string> Pending { get; set; } = [.. WikiUrls.TypeIndexSlugs];

    public TaskCompletionSource? Gate { get; set; }

    public Action<SyncProgress>? Progress { get; private set; }

    public Action<SyncUpdate>? Saved { get; private set; }

    public CancellationToken Token { get; private set; }

    public int Calls { get; private set; }

    public bool Throw { get; set; }

    public IReadOnlyList<string> GetPending() => Pending;

    public async Task<SyncResult> RunAsync(Action<SyncProgress>? progress, Action<SyncUpdate>? saved, CancellationToken ct)
    {
        Calls++;
        Progress = progress;
        Saved = saved;
        Token = ct;
        if (Throw)
        {
            throw new InvalidOperationException("boom");
        }

        if (Gate is not null)
        {
            await Gate.Task.ConfigureAwait(false);
        }

        return Result;
    }
}

/// <summary>Hand-written fake wiki HTML shaped like the real pages.</summary>
public static class WikiPages
{
    public static string HardFarm(params (string Group, string[] Slugs)[] groups) =>
        "<html><body><div id=\"page-content\"><p><em>Created by Tester, Revised by Checker</em></p>"
        + string.Concat(groups.Select(g => $"<h1><span>{g.Group}</span></h1>"
            + string.Concat(g.Slugs.Select(s => $"<a href=\"http://aqwwiki.wikidot.com/{s}\">http://aqwwiki.wikidot.com/{s}</a><br />\n"))))
        + "</div></body></html>";

    public static string Index(params (string Slug, string Name)[] items) =>
        "<html><body><div id=\"page-content\"><div class=\"list-pages-box\">"
        + string.Concat(items.Select(i => $"<div class=\"list-pages-item\"><p><a href=\"/{i.Slug}\">{i.Name}</a></p></div>"))
        + "</div></div></body></html>";

    public static string[] Slugs(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => $"{prefix}-{i}").ToArray();
}

/// <summary>A clock that only moves when the test (or a fake fetcher) advances it; it also serves as the timestamp source.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by)
    {
        _now += by;
        _ticks += by.Ticks;
    }
}
