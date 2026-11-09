using System.Text;
using System.Text.Json;
using AqwCompanion.Core.Feed;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Feed;

public class FeedHandlerTests
{
    private const string Host = "127.0.0.1:47821";
    private const string Secret = "s3cr3t-value-for-tests";

    private readonly FakeFeedSource _source = new();

    private FeedHandler Build(string secret = Secret) => new(() => Encoding.UTF8.GetBytes(secret), _source, Host, "9.9.9");

    private static FeedRequest Request(string method, string path, params (string Name, string Value)[] headers) =>
        new(method, path, headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase));

    private static FeedRequest Get(string path, string bearer = Secret, string host = Host) =>
        Request("GET", path, ("Host", host), ("Authorization", "Bearer " + bearer));

    private static string Text(FeedResponse response) => Encoding.UTF8.GetString(response.Body);

    [Fact]
    public void Ping_names_the_app_the_contract_and_the_version()
    {
        var response = Build().Handle(Get("/v1/ping"));

        Assert.Equal(200, response.Status);
        Assert.Equal("application/json; charset=utf-8", response.ContentType);
        Assert.Equal("""{"contract":1,"app":"Questkeeper","version":"9.9.9"}""", Text(response));
    }

    [Fact]
    public void State_is_the_current_snapshot_as_json()
    {
        _source.Current = FeedSnapshot.Empty with { Selected = "Alpha Hero" };

        var response = Build().Handle(Get("/v1/state"));

        Assert.Equal(200, response.Status);
        Assert.Equal(FeedJson.Serialize(_source.Current), Text(response));
        Assert.False(response.IsEventStream);
    }

    [Fact]
    public void Events_is_flagged_as_a_stream_with_no_body()
    {
        var response = Build().Handle(Get("/v1/events"));

        Assert.Equal(200, response.Status);
        Assert.True(response.IsEventStream);
        Assert.Equal("text/event-stream", response.ContentType);
        Assert.Empty(response.Body);
    }

    [Theory]
    [InlineData("Bearer wrong")]
    [InlineData("Bearer ")]
    [InlineData("Bearer")]
    [InlineData("Basic " + Secret)]
    [InlineData(Secret)]
    [InlineData("")]
    public void A_wrong_or_malformed_authorization_is_refused(string header)
    {
        var response = Build().Handle(Request("GET", "/v1/state", ("Host", Host), ("Authorization", header)));

        Assert.Equal(401, response.Status);
        Assert.DoesNotContain(Secret, Text(response));
    }

    [Fact]
    public void A_missing_authorization_is_refused()
    {
        Assert.Equal(401, Build().Handle(Request("GET", "/v1/state", ("Host", Host))).Status);
    }

    [Fact]
    public void An_empty_secret_refuses_everything_even_an_empty_bearer()
    {
        Assert.Equal(401, Build("").Handle(Get("/v1/state", bearer: "")).Status);
    }

    [Theory]
    [InlineData("localhost:47821")]
    [InlineData("127.0.0.1:1234")]
    [InlineData("evil.example")]
    [InlineData("")]
    public void A_wrong_host_is_refused_before_anything_else(string host)
    {
        var response = Build().Handle(Request("GET", "/v1/state", ("Host", host), ("Authorization", "nonsense")));

        Assert.Equal(421, response.Status);
    }

    [Fact]
    public void A_missing_host_is_refused()
    {
        Assert.Equal(421, Build().Handle(Request("GET", "/v1/state", ("Authorization", "Bearer " + Secret))).Status);
    }

    [Fact]
    public void A_request_from_a_web_page_is_refused_even_with_the_secret()
    {
        var request = Request("GET", "/v1/state", ("Host", Host), ("Authorization", "Bearer " + Secret), ("Origin", "https://evil.example"));

        Assert.Equal(403, Build().Handle(request).Status);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public void Only_get_is_accepted(string method)
    {
        var response = Build().Handle(Request(method, "/v1/state", ("Host", Host), ("Authorization", "Bearer " + Secret)));

        Assert.Equal(405, response.Status);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/v1")]
    [InlineData("/v2/state")]
    [InlineData("/v1/state/extra")]
    public void Anything_else_is_not_found(string path)
    {
        Assert.Equal(404, Build().Handle(Get(path)).Status);
    }

    [Fact]
    public void The_query_string_and_the_case_of_the_path_do_not_change_the_route()
    {
        Assert.Equal(200, Build().Handle(Get("/v1/state?x=1")).Status);
        Assert.Equal(200, Build().Handle(Get("/V1/Ping")).Status);
    }

    [Fact]
    public void Header_names_are_matched_ignoring_case()
    {
        var request = new FeedRequest(
            "GET", "/v1/ping",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["host"] = Host, ["authorization"] = "Bearer " + Secret });

        Assert.Equal(200, Build().Handle(request).Status);
    }

    [Fact]
    public void Errors_are_json_with_a_fixed_phrase()
    {
        var response = Build().Handle(Get("/nope"));

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("not found", json.RootElement.GetProperty("error").GetString());
    }
}
