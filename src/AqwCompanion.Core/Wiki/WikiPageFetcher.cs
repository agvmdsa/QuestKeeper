using System.Net;
using System.Text;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Ingestion;
using Microsoft.Extensions.Logging;

namespace AqwCompanion.Core.Wiki;

/// <summary>
/// One read-only GET against aqwwiki.wikidot.com. https and the wiki host only (also for redirects, validated hop by
/// hop, at most 3), a per-call timeout (30 s by default), an 8 MiB cap, text/html only (text/plain for robots.txt). No cookies, no
/// credentials. The caller supplies an <see cref="HttpClient"/> with automatic redirects and cookies turned off.
/// Pacing between requests and the robots.txt rules are the caller's job.
/// </summary>
public sealed class WikiPageFetcher(HttpClient http, string appVersion, ILogger<WikiPageFetcher> logger) : IWikiPageFetcher
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(120);

    /// <summary>No value means the default; anything else is held between one second and two minutes (never zero, negative or infinite).</summary>
    public static TimeSpan NormalizeTimeout(TimeSpan? timeout) =>
        timeout is not { } value ? DefaultTimeout : value == Timeout.InfiniteTimeSpan ? MaxTimeout : value < MinTimeout ? MinTimeout : value > MaxTimeout ? MaxTimeout : value;

    private const int MaxRedirects = 3;
    private const string HtmlType = "text/html";
    private const string TextType = "text/plain";

    public Task<PageFetchResult> FetchPageAsync(string url, TimeSpan? timeout = null, CancellationToken ct = default) =>
        FetchAsync(url, HtmlType, NormalizeTimeout(timeout), ct);

    public Task<PageFetchResult> FetchRobotsAsync(CancellationToken ct = default) =>
        FetchAsync(WikiUrls.Robots, TextType, DefaultTimeout, ct);

    private async Task<PageFetchResult> FetchAsync(string url, string expectedType, TimeSpan timeout, CancellationToken ct)
    {
        if (!TryAllow(url, null, out var uri))
        {
            // The address is not trusted, so it is not logged at all.
            logger.LogWarning("Wiki fetch failed: {Kind} {Page}", PageFailureKind.Blocked, "(not a wiki page)");
            return PageFetchResult.Failure("Only https pages on aqwwiki.wikidot.com can be read.", PageFailureKind.Blocked);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", $"AqwCompanion/{appVersion} (read-only personal companion)");
                request.Headers.TryAddWithoutValidation("Accept", expectedType);

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                    .ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    if (response.Headers.Location is null)
                    {
                        return Fail(uri, PageFailureKind.Http, $"The server answered HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
                    }

                    if (!TryAllow(response.Headers.Location.OriginalString, uri, out var next))
                    {
                        return Fail(uri, PageFailureKind.Blocked, "The server redirected to a page that is not allowed.");
                    }

                    uri = next;
                    continue;
                }

                return await ReadAsync(response, uri, expectedType, deadline.Token).ConfigureAwait(false);
            }

            return Fail(uri, PageFailureKind.Network, "The server redirected too many times.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail(uri, PageFailureKind.Timeout, "The request timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            // Type only: exception messages can contain the request URL.
            logger.LogWarning("Wiki fetch failed: {Kind} {Page} {ExceptionType}", PageFailureKind.Network, PageLabel(uri), ex.GetType().Name);
            return PageFetchResult.Failure("The request failed.", PageFailureKind.Network);
        }
    }

    private async Task<PageFetchResult> ReadAsync(HttpResponseMessage response, Uri uri, string expectedType, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Fail(uri, PageFailureKind.Http, $"The server answered HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
        }

        if (!(response.Content.Headers.ContentType?.MediaType).EqualsIgnoreCase(expectedType))
        {
            return Fail(
                uri,
                PageFailureKind.NotHtml,
                expectedType == HtmlType
                    ? "The server did not answer with an HTML page."
                    : "The server did not answer with a text file.");
        }

        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            return Fail(uri, PageFailureKind.TooLarge, "The page is larger than the allowed size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return Fail(uri, PageFailureKind.TooLarge, "The page is larger than the allowed size.");
            }

            buffer.Write(chunk, 0, read);
        }

        var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return PageFetchResult.Success(PageSnapshot.Create(uri.AbsoluteUri, text, appVersion, DateTimeOffset.UtcNow));
    }

    // Logs the kind and the page (path only: no query, no content); the wiki's page names are public and validated.
    private PageFetchResult Fail(Uri uri, PageFailureKind kind, string message, int? status = null)
    {
        logger.LogWarning("Wiki fetch failed: {Kind} {Page}", kind, PageLabel(uri));
        return PageFetchResult.Failure(message, kind, status);
    }

    private static string PageLabel(Uri uri)
    {
        var slug = WikiUrls.Slug(uri.AbsolutePath);
        return slug is not null && (WikiUrls.IsValidSlug(slug) || slug == WikiUrls.RobotsSlug) ? "/" + slug : "(page)";
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool TryAllow(string? candidate, Uri? baseUri, out Uri uri)
    {
        uri = null!;
        if (candidate is null)
        {
            return false;
        }

        var ok = baseUri is null
            ? Uri.TryCreate(candidate, UriKind.Absolute, out var absolute)
            : Uri.TryCreate(baseUri, candidate, out absolute);

        if (!ok || absolute is null || !WikiUrls.IsWikiPage(absolute.AbsoluteUri))
        {
            return false;
        }

        uri = absolute;
        return true;
    }
}
