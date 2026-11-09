using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AqwCompanion.Core.Feed;

/// <summary>A request as the feed sees it: the method, the path (with or without a query) and the headers.</summary>
public sealed record FeedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers);

/// <summary>The answer. For an event stream the body is empty and <see cref="IsEventStream"/> tells the host to keep the connection open.</summary>
public sealed record FeedResponse(int Status, string ContentType, byte[] Body, bool IsEventStream = false);

/// <summary>
/// The rules of the local feed, with no sockets in it. A request is checked in this order, so a wrong caller learns as little as
/// possible: the host header (421), a web page's Origin (403), the method (405), the bearer secret (401), then the route.
/// </summary>
public sealed class FeedHandler(Func<byte[]> secret, IFeedSource source, string expectedHost, string appVersion)
{
    private const string Json = "application/json; charset=utf-8";
    private const string BearerPrefix = "Bearer ";

    public FeedResponse Handle(FeedRequest request)
    {
        if (!string.Equals(Header(request, "Host"), expectedHost, StringComparison.Ordinal))
        {
            return Error(421, "wrong host");
        }

        if (Header(request, "Origin") is not null)
        {
            return Error(403, "forbidden origin");
        }

        if (!string.Equals(request.Method, "GET", StringComparison.Ordinal))
        {
            return Error(405, "method not allowed");
        }

        if (!IsAuthorized(request))
        {
            return Error(401, "unauthorized");
        }

        var path = request.Path;
        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            path = path[..query];
        }

        if (path.Equals("/v1/ping", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(JsonSerializer.Serialize(new { contract = FeedSnapshot.ContractNumber, app = "Questkeeper", version = appVersion }, FeedJson.Options));
        }

        if (path.Equals("/v1/state", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(FeedJson.Serialize(source.Current));
        }

        if (path.Equals("/v1/events", StringComparison.OrdinalIgnoreCase))
        {
            return new FeedResponse(200, "text/event-stream", [], IsEventStream: true);
        }

        return Error(404, "not found");
    }

    private bool IsAuthorized(FeedRequest request)
    {
        var expected = secret();
        var header = Header(request, "Authorization");
        if (expected.Length == 0 || header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(header[BearerPrefix.Length..]);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    private static string? Header(FeedRequest request, string name)
    {
        foreach (var (key, value) in request.Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static FeedResponse Ok(string json) => new(200, Json, Encoding.UTF8.GetBytes(json));

    private static FeedResponse Error(int status, string phrase) =>
        new(status, Json, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = phrase }, FeedJson.Options)));
}
