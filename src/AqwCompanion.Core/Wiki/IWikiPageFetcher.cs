using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Core.Wiki;

/// <summary>Reads pages of the AQW Wiki with single GETs. Implementations must not throw for network or HTTP failures.</summary>
public interface IWikiPageFetcher
{
    /// <summary>
    /// One https wiki page, text/html only. <paramref name="timeout"/> is the deadline of this call (30 s when null); a deadline
    /// reached while <paramref name="ct"/> was not cancelled is a <see cref="PageFailureKind.Timeout"/> failure.
    /// </summary>
    Task<PageFetchResult> FetchPageAsync(string url, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>The wiki's robots.txt, text/plain only.</summary>
    Task<PageFetchResult> FetchRobotsAsync(CancellationToken ct = default);
}
