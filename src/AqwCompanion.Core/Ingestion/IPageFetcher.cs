namespace AqwCompanion.Core.Ingestion;

/// <summary>Why a fetch failed; <see cref="None"/> for a success.</summary>
public enum PageFailureKind
{
    None,

    /// <summary>The deadline passed while the caller had not cancelled.</summary>
    Timeout,

    /// <summary>The connection failed, or a redirect loop ended the request.</summary>
    Network,

    /// <summary>The server answered with a non-success status; see <see cref="PageFetchResult.HttpStatus"/>.</summary>
    Http,

    /// <summary>The address (or a redirect target) is not one the companion may read.</summary>
    Blocked,

    TooLarge,

    NotHtml,
}

/// <summary>
/// Outcome of fetching one page. <see cref="Error"/> is a short message that never contains the URL query.
/// <see cref="Kind"/> and <see cref="HttpStatus"/> are optional extras: a plain <see cref="Failure(string)"/> counts as <see cref="PageFailureKind.Network"/>.
/// </summary>
public sealed record PageFetchResult(PageSnapshot? Snapshot, string? Error, PageFailureKind Kind = PageFailureKind.None, int? HttpStatus = null)
{
    public bool IsSuccess => Snapshot is not null;

    public static PageFetchResult Success(PageSnapshot snapshot) => new(snapshot, null);

    public static PageFetchResult Failure(string error) => new(null, error, PageFailureKind.Network);

    public static PageFetchResult Failure(string error, PageFailureKind kind, int? httpStatus = null) =>
        new(null, error, kind, httpStatus);
}

/// <summary>Reads one public page with a single GET. Implementations must not throw for network or HTTP failures.</summary>
public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(string url, CancellationToken ct = default);
}
