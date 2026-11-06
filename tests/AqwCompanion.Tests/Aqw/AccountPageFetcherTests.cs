using System.Net;
using System.Text;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Aqw;

public class AccountPageFetcherTests
{
    private const string BadgesUrl = "https://account.aq.com/CharPage/Badges?ccid=3";
    private const string CharPageUrl = "https://account.aq.com/CharPage?id=Test%20Hero";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Reply(string body, string mediaType, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static (AccountPageFetcher Fetcher, StubHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new AccountPageFetcher(new HttpClient(handler), "1.0.0", new ListLogger<AccountPageFetcher>()), handler);
    }

    [Fact]
    public async Task Json_is_accepted_for_the_badges_url()
    {
        var (fetcher, handler) = Build(_ => Reply("[]", "application/json"));

        var result = await fetcher.FetchAsync(BadgesUrl);

        Assert.True(result.IsSuccess);
        Assert.Equal("[]", result.Snapshot!.Payload);
        Assert.Equal("https://account.aq.com/CharPage/Badges", result.Snapshot.Url);
        Assert.Equal("application/json", Assert.Single(handler.Requests).Headers.Accept.ToString());
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    [InlineData("application/javascript")]
    public async Task A_wrong_content_type_is_refused_for_the_badges_url(string mediaType)
    {
        var (fetcher, _) = Build(_ => Reply("[]", mediaType));

        var result = await fetcher.FetchAsync(BadgesUrl);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Json_is_refused_for_a_url_that_is_not_the_badges_list()
    {
        var (fetcher, handler) = Build(_ => Reply("[]", "application/json"));

        var result = await fetcher.FetchAsync(CharPageUrl);

        Assert.False(result.IsSuccess);
        Assert.Equal("text/html", Assert.Single(handler.Requests).Headers.Accept.ToString());
    }

    [Fact]
    public async Task Html_pages_are_still_accepted_for_other_urls()
    {
        var (fetcher, _) = Build(_ => Reply("<html/>", "text/html"));

        var result = await fetcher.FetchAsync(CharPageUrl);

        Assert.True(result.IsSuccess);
        Assert.Equal("<html/>", result.Snapshot!.Payload);
    }

    [Theory]
    [InlineData("https://evil.example.com/CharPage/Badges?ccid=3")]
    [InlineData("http://account.aq.com/CharPage/Badges?ccid=3")]
    [InlineData("https://account.aq.com.evil.example/CharPage/Badges?ccid=3")]
    public async Task Other_hosts_and_schemes_are_refused_without_a_request(string url)
    {
        var (fetcher, handler) = Build(_ => Reply("[]", "application/json"));

        var result = await fetcher.FetchAsync(url);

        Assert.False(result.IsSuccess);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_redirect_off_the_account_host_is_refused()
    {
        var (fetcher, handler) = Build(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example.com/CharPage/Badges?ccid=3");
            return redirect;
        });

        var result = await fetcher.FetchAsync(BadgesUrl);

        Assert.False(result.IsSuccess);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_redirect_from_the_badges_url_to_a_page_that_answers_json_is_refused()
    {
        var calls = 0;
        var (fetcher, _) = Build(_ =>
        {
            if (calls++ == 0)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://account.aq.com/Home");
                return redirect;
            }

            return Reply("[]", "application/json");
        });

        Assert.False((await fetcher.FetchAsync(BadgesUrl)).IsSuccess);
    }

    [Fact]
    public async Task A_json_answer_larger_than_the_cap_is_refused()
    {
        var (fetcher, _) = Build(_ => Reply(new string('x', AccountPageFetcher.MaxBytes + 1), "application/json"));

        Assert.False((await fetcher.FetchAsync(BadgesUrl)).IsSuccess);
    }

    [Fact]
    public async Task An_http_error_is_a_failure()
    {
        var (fetcher, _) = Build(_ => Reply("error", "application/json", HttpStatusCode.InternalServerError));

        Assert.False((await fetcher.FetchAsync(BadgesUrl)).IsSuccess);
    }
}
