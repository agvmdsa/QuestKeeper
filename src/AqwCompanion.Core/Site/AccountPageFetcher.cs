using System.Net;
using System.Text;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Ingestion;
using Microsoft.Extensions.Logging;

namespace AqwCompanion.Core.Site;

/// <summary>
/// One read-only GET against account.aq.com. https and the account host only (also for redirects, which are
/// validated hop by hop), a hard timeout, a size cap and text/html only. The exceptions are the public badges and
/// inventory lists, which are answered as application/json (and only that). No cookies, no credentials.
/// The caller supplies an <see cref="HttpClient"/> with automatic redirects and cookies turned off.
/// </summary>
public sealed class AccountPageFetcher(HttpClient http, string appVersion, ILogger<AccountPageFetcher> logger) : IPageFetcher
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const int MaxRedirects = 3;
    private const string HtmlType = "text/html";
    private const string JsonType = "application/json";

    public async Task<PageFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!TryAllow(url, null, out var uri))
        {
            return PageFetchResult.Failure("Only https pages on account.aq.com can be read.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                var expectedType = ExpectedType(uri);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", $"AqwCompanion/{appVersion} (read-only personal companion)");
                request.Headers.TryAddWithoutValidation("Accept", expectedType);

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                    .ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    if (!TryAllow(response.Headers.Location?.OriginalString, uri, out var next))
                    {
                        return Fail("The server redirected to a page that is not allowed.");
                    }

                    uri = next;
                    continue;
                }

                return await ReadAsync(response, uri, deadline.Token).ConfigureAwait(false);
            }

            return Fail("The server redirected too many times.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("The request timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            // Type only: exception messages can contain the request URL.
            logger.LogWarning("Page fetch failed: {ExceptionType}", ex.GetType().Name);
            return PageFetchResult.Failure("The request failed.");
        }
    }

    private async Task<PageFetchResult> ReadAsync(HttpResponseMessage response, Uri uri, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Fail($"The server answered HTTP {(int)response.StatusCode}.");
        }

        var expectedType = ExpectedType(uri);
        if (!(response.Content.Headers.ContentType?.MediaType).EqualsIgnoreCase(expectedType))
        {
            return Fail(expectedType == JsonType
                ? "The server did not answer with JSON data."
                : "The server did not answer with an HTML page.");
        }

        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            return Fail("The page is larger than the allowed size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return Fail("The page is larger than the allowed size.");
            }

            buffer.Write(chunk, 0, read);
        }

        var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return PageFetchResult.Success(PageSnapshot.Create(uri.AbsoluteUri, text, appVersion, DateTimeOffset.UtcNow));
    }

    /// <summary>JSON for the badges list and the inventory list, HTML for everything else; decided per hop, so a redirect cannot widen it.</summary>
    private static string ExpectedType(Uri uri) => AqwUrls.IsBadges(uri.AbsoluteUri) || AqwUrls.IsInventory(uri.AbsoluteUri) ? JsonType : HtmlType;

    private PageFetchResult Fail(string message)
    {
        logger.LogWarning("Page fetch failed: {Reason}", message);
        return PageFetchResult.Failure(message);
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

        if (!ok
            || absolute is null
            || absolute.Scheme != Uri.UriSchemeHttps
            || !absolute.IsDefaultPort
            || absolute.UserInfo.Length != 0
            || !absolute.Host.EqualsIgnoreCase(AqwUrls.AccountHost))
        {
            return false;
        }

        uri = absolute;
        return true;
    }
}
