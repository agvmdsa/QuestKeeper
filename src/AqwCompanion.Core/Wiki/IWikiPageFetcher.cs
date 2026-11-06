using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Core.Wiki;

/// <summary>Reads pages of the AQW Wiki with single GETs. Implementations must not throw for network or HTTP failures.</summary>
public interface IWikiPageFetcher
{
    /// <summary>One https wiki page, text/html only.</summary>
    Task<PageFetchResult> FetchPageAsync(string url, CancellationToken ct = default);

    /// <summary>The wiki's robots.txt, text/plain only.</summary>
    Task<PageFetchResult> FetchRobotsAsync(CancellationToken ct = default);
}
