using AqwCompanion.App.HardFarms;
using AqwCompanion.App.SignIn;
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

    public List<string> Requested => Log.Where(e => e.StartsWith("fetch:", StringComparison.Ordinal)).Select(e => e[6..]).ToList();

    public Task<PageFetchResult> FetchRobotsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Log.Add("fetch:robots.txt");
        return Task.FromResult(RobotsError is { } error
            ? PageFetchResult.Failure(error)
            : PageFetchResult.Success(PageSnapshot.Create(WikiUrls.Robots, Robots, "1.0.0", DateTimeOffset.UnixEpoch)));
    }

    public Task<PageFetchResult> FetchPageAsync(string url, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var slug = WikiUrls.Slug(url)!;
        Log.Add("fetch:" + slug);
        OnFetch?.Invoke(slug);
        if (Errors.TryGetValue(slug, out var error))
        {
            return Task.FromResult(PageFetchResult.Failure(error));
        }

        return Task.FromResult(Pages.TryGetValue(slug, out var html)
            ? PageFetchResult.Success(PageSnapshot.Create(url, html, "1.0.0", DateTimeOffset.UnixEpoch))
            : PageFetchResult.Failure("HTTP 404"));
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

/// <summary>An updater whose result the test controls; <see cref="Gate"/> holds it open to observe the Updating state.</summary>
public sealed class FakeHardFarmUpdater : IHardFarmUpdater
{
    public HardFarmUpdateResult Result { get; set; } = new(HardFarmUpdateStatus.Failed, "not set");

    public TaskCompletionSource? Gate { get; set; }

    public Action<HardFarmProgress>? Progress { get; private set; }

    public CancellationToken Token { get; private set; }

    public int Calls { get; private set; }

    public bool Throw { get; set; }

    public async Task<HardFarmUpdateResult> UpdateAsync(Action<HardFarmProgress>? progress, CancellationToken ct)
    {
        Calls++;
        Progress = progress;
        Token = ct;
        if (Throw)
        {
            throw new InvalidOperationException("boom");
        }

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(ct).ConfigureAwait(false);
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
