using System.Net;
using System.Text;
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
        Assert.Equal(TimeSpan.FromSeconds(20), WikiPageFetcher.Timeout);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchPageAsync(PageUrl, cts.Token));
    }
}
