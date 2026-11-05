namespace AqwCompanion.Core.Ingestion;

/// <summary>Outcome of fetching one page. <see cref="Error"/> is a short message that never contains the URL query.</summary>
public sealed record PageFetchResult(PageSnapshot? Snapshot, string? Error)
{
    public bool IsSuccess => Snapshot is not null;

    public static PageFetchResult Success(PageSnapshot snapshot) => new(snapshot, null);

    public static PageFetchResult Failure(string error) => new(null, error);
}

/// <summary>Reads one public page with a single GET. Implementations must not throw for network or HTTP failures.</summary>
public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(string url, CancellationToken ct = default);
}
