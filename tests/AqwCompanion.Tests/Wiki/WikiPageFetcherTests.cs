using System.Net;
using System.Text;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Wiki;

public class WikiPageFetcherTests
{
    private const string PageUrl = "https://aqwwiki.wikidot.com/syfarm";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private static HttpResponseMessage Reply(string body, string mediaType = "text/html", HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static HttpResponseMessage Redirect(string to)
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return redirect;
    }

    private static (WikiPageFetcher Fetcher, StubHandler Handler, ListLogger<WikiPageFetcher> Log) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var log = new ListLogger<WikiPageFetcher>();
        return (new WikiPageFetcher(new HttpClient(handler), "1.2.3", log), handler, log);
    }

    [Fact]
    public async Task An_html_page_is_returned_with_the_identifying_headers_and_nothing_else()
    {
        var (fetcher, handler, _) = Build(_ => Reply("<html/>"));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.True(result.IsSuccess);
        Assert.Equal("<html/>", result.Snapshot!.Payload);
        Assert.Equal(PageUrl, result.Snapshot.Url);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("AqwCompanion/1.2.3 (read-only personal companion)", request.Headers.UserAgent.ToString());
        Assert.Equal("text/html", request.Headers.Accept.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task Robots_txt_is_fetched_from_the_fixed_address_and_accepted_as_plain_text()
    {
        var (fetcher, handler, _) = Build(_ => Reply("User-agent: *\n", "text/plain"));

        var result = await fetcher.FetchRobotsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("User-agent: *\n", result.Snapshot!.Payload);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://aqwwiki.wikidot.com/robots.txt", request.RequestUri!.AbsoluteUri);
        Assert.Equal("text/plain", request.Headers.Accept.ToString());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("application/xhtml+xml")]
    public async Task A_page_that_is_not_html_is_refused(string mediaType)
    {
        var (fetcher, _, _) = Build(_ => Reply("x", mediaType));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Robots_txt_served_as_html_is_refused()
    {
        var (fetcher, _, _) = Build(_ => Reply("<html/>", "text/html"));

        Assert.False((await fetcher.FetchRobotsAsync()).IsSuccess);
    }

    [Fact]
    public async Task An_html_content_type_with_a_charset_is_accepted()
    {
        var (fetcher, _, _) = Build(_ =>
        {
            var response = Reply("<p/>");
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html; charset=utf-8");
            return response;
        });

        Assert.True((await fetcher.FetchPageAsync(PageUrl)).IsSuccess);
    }

    [Theory]
    [InlineData("http://aqwwiki.wikidot.com/syfarm")]
    [InlineData("https://evil.example.com/syfarm")]
    [InlineData("https://aqwwiki.wikidot.com.evil.example/syfarm")]
    [InlineData("https://account.aq.com/Home")]
    [InlineData("https://aqwwiki.wikidot.com:8443/syfarm")]
    [InlineData("https://user:pw@aqwwiki.wikidot.com/syfarm")]
    [InlineData("/syfarm")]
    [InlineData("")]
    public async Task Other_hosts_schemes_and_forms_are_refused_without_a_request(string url)
    {
        var (fetcher, handler, _) = Build(_ => Reply("<html/>"));

        var result = await fetcher.FetchPageAsync(url);

        Assert.False(result.IsSuccess);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://evil.example.com/syfarm")]
    [InlineData("http://aqwwiki.wikidot.com/syfarm")]
    [InlineData("https://account.aq.com/Home")]
    public async Task A_redirect_to_a_forbidden_target_is_refused(string target)
    {
        var (fetcher, handler, _) = Build(_ => Redirect(target));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.False(result.IsSuccess);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Redirects_inside_the_wiki_are_followed_up_to_three_hops()
    {
        var calls = 0;
        var (fetcher, handler, _) = Build(_ => calls++ < 3 ? Redirect("/next" + calls) : Reply("<p/>"));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("https://aqwwiki.wikidot.com/next3", result.Snapshot!.Url);
    }

    [Fact]
    public async Task A_fourth_redirect_hop_is_refused()
    {
        var (fetcher, handler, _) = Build(_ => Redirect("/again"));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.False(result.IsSuccess);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task A_page_over_the_cap_is_refused_while_one_at_the_cap_is_accepted()
    {
        var (big, _, _) = Build(_ => Reply(new string('x', WikiPageFetcher.MaxBytes + 1)));
        var (exact, _, _) = Build(_ => Reply(new string('x', WikiPageFetcher.MaxBytes)));

        Assert.False((await big.FetchPageAsync(PageUrl)).IsSuccess);
        Assert.True((await exact.FetchPageAsync(PageUrl)).IsSuccess);
    }

    [Fact]
    public void The_limits_are_the_agreed_ones()
    {
        Assert.Equal(8 * 1024 * 1024, WikiPageFetcher.MaxBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), WikiPageFetcher.DefaultTimeout);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_http_error_is_a_failure_that_names_the_status(HttpStatusCode status)
    {
        var (fetcher, _, _) = Build(_ => Reply("nope", status: status));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.False(result.IsSuccess);
        Assert.Contains(((int)status).ToString(), result.Error);
    }

    [Fact]
    public async Task A_network_error_is_a_failure_logged_by_exception_type_only()
    {
        var (fetcher, _, log) = Build(_ => throw new HttpRequestException("secret https://aqwwiki.wikidot.com/syfarm?q=1"));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("secret", result.Error);
        Assert.All(log.Entries, entry => Assert.DoesNotContain("secret", entry.Message));
        Assert.Contains(log.Entries, entry => entry.Message.Contains(nameof(HttpRequestException)));
    }

    [Fact]
    public async Task A_cancelled_request_is_reported_by_cancellation_not_as_a_failure_result()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var fetcher = new WikiPageFetcher(new HttpClient(new HangingHandler()), "1.0.0", new ListLogger<WikiPageFetcher>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchPageAsync(PageUrl, null, cts.Token));
    }

    [Fact]
    public async Task A_page_that_does_not_answer_in_time_is_a_timeout_failure_not_a_cancellation()
    {
        var fetcher = new WikiPageFetcher(new HttpClient(new HangingHandler()), "1.0.0", new ListLogger<WikiPageFetcher>());

        var result = await fetcher.FetchPageAsync(PageUrl, TimeSpan.FromMilliseconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(PageFailureKind.Timeout, result.Kind);
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public async Task The_timeout_is_per_call_and_a_generous_one_lets_a_slow_answer_through()
    {
        var (fetcher, _, _) = Build(_ => Reply("<p/>"));

        var result = await fetcher.FetchPageAsync(PageUrl, TimeSpan.FromMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(PageFailureKind.None, result.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_non_success_answer_is_an_http_failure_with_its_status(HttpStatusCode status)
    {
        var (fetcher, _, _) = Build(_ => Reply("nope", status: status));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.Equal(PageFailureKind.Http, result.Kind);
        Assert.Equal((int)status, result.HttpStatus);
    }

    [Fact]
    public async Task A_connection_error_is_a_network_failure()
    {
        var (fetcher, _, _) = Build(_ => throw new HttpRequestException("down"));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.Equal(PageFailureKind.Network, result.Kind);
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public async Task A_page_that_is_not_html_is_a_not_html_failure()
    {
        var (fetcher, _, _) = Build(_ => Reply("x", "application/json"));

        Assert.Equal(PageFailureKind.NotHtml, (await fetcher.FetchPageAsync(PageUrl)).Kind);
    }

    [Fact]
    public async Task A_page_over_the_cap_is_a_too_large_failure()
    {
        var (fetcher, _, _) = Build(_ => Reply(new string('x', WikiPageFetcher.MaxBytes + 1)));

        Assert.Equal(PageFailureKind.TooLarge, (await fetcher.FetchPageAsync(PageUrl)).Kind);
    }

    [Theory]
    [InlineData("http://aqwwiki.wikidot.com/syfarm")]
    [InlineData("https://evil.example.com/syfarm")]
    public async Task A_forbidden_address_is_a_blocked_failure(string url)
    {
        var (fetcher, _, _) = Build(_ => Reply("<p/>"));

        Assert.Equal(PageFailureKind.Blocked, (await fetcher.FetchPageAsync(url)).Kind);
    }

    [Fact]
    public async Task A_redirect_to_a_forbidden_target_is_a_blocked_failure()
    {
        var (fetcher, _, _) = Build(_ => Redirect("https://evil.example.com/x"));

        Assert.Equal(PageFailureKind.Blocked, (await fetcher.FetchPageAsync(PageUrl)).Kind);
    }

    [Fact]
    public async Task A_redirect_without_a_location_is_an_http_failure_not_a_blocked_one()
    {
        var (fetcher, _, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Found));

        var result = await fetcher.FetchPageAsync(PageUrl);

        Assert.Equal(PageFailureKind.Http, result.Kind);
        Assert.Equal(302, result.HttpStatus);
    }

    [Theory]
    [InlineData("http://aqwwiki.wikidot.com/next")]
    [InlineData("https://other.example.com/next")]
    public async Task A_redirect_to_another_host_or_to_http_stays_blocked(string target)
    {
        var (fetcher, _, _) = Build(_ => Redirect(target));

        Assert.Equal(PageFailureKind.Blocked, (await fetcher.FetchPageAsync(PageUrl)).Kind);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(0.0, 1)]
    [InlineData(-5.0, 1)]
    [InlineData(0.2, 1)]
    [InlineData(45.0, 45)]
    [InlineData(500.0, 120)]
    public void The_timeout_is_defaulted_and_clamped_between_one_and_120_seconds(double? seconds, double expected)
    {
        var timeout = seconds is { } s ? TimeSpan.FromSeconds(s) : (TimeSpan?)null;

        Assert.Equal(TimeSpan.FromSeconds(expected), WikiPageFetcher.NormalizeTimeout(timeout));
    }

    [Fact]
    public void An_infinite_timeout_is_clamped_to_the_maximum()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), WikiPageFetcher.NormalizeTimeout(Timeout.InfiniteTimeSpan));
        Assert.Equal(TimeSpan.FromSeconds(120), WikiPageFetcher.NormalizeTimeout(TimeSpan.MaxValue));
    }

    [Fact]
    public async Task The_failure_log_names_the_kind_and_the_page_but_never_the_query_or_the_content()
    {
        var (fetcher, _, log) = Build(_ => Reply("SECRET-CONTENT", status: HttpStatusCode.ServiceUnavailable));

        await fetcher.FetchPageAsync(PageUrl + "?token=SECRET-QUERY");

        var entry = Assert.Single(log.Entries);
        Assert.Contains("Http", entry.Message);
        Assert.Contains("/syfarm", entry.Message);
        Assert.DoesNotContain("SECRET", entry.Message);
        Assert.DoesNotContain("token", entry.Message);
    }

    [Fact]
    public async Task A_timeout_is_logged_with_its_kind_and_page()
    {
        var log = new ListLogger<WikiPageFetcher>();
        var fetcher = new WikiPageFetcher(new HttpClient(new HangingHandler()), "1.0.0", log);

        await fetcher.FetchPageAsync("https://aqwwiki.wikidot.com/pets", TimeSpan.FromMilliseconds(20));

        Assert.Contains(log.Entries, e => e.Message.Contains("Timeout") && e.Message.Contains("/pets"));
    }

    [Fact]
    public async Task Robots_txt_failures_are_classified_too()
    {
        var (fetcher, _, _) = Build(_ => Reply("x", status: HttpStatusCode.NotFound));

        var result = await fetcher.FetchRobotsAsync();

        Assert.Equal(PageFailureKind.Http, result.Kind);
        Assert.Equal(404, result.HttpStatus);
    }
}
