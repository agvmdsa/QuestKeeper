using System.Text;
using System.Text.Json;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;

namespace AqwCompanion.Phone.Web;

public sealed record PhoneRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body);

public sealed record PhoneResponse(
    int Status,
    string ContentType,
    byte[] Body,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    bool IsEventStream = false,
    string? DeviceId = null);

public sealed record Asset(byte[] Bytes, string ContentType, string Etag);

public interface IAssetSource
{
    /// <summary>The embedded file for a path such as <c>/index.html</c>, or null.</summary>
    Asset? Find(string path);
}

/// <summary>
/// The rules of the phone web host, with no sockets in it. Order: Host (421), static files, then for <c>/api/*</c> the Origin (403),
/// the write header (403), pairing, the device cookie (401), the method (405) and the route (404).
/// </summary>
public sealed class PhoneHandler(PhoneRegistry registry, PhoneHub hub, IAssetSource assets, Func<string, bool> isAllowedHost, string appVersion)
{
    public const string CookieName = "qkp_device";
    private const string Csp = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public PhoneResponse Handle(PhoneRequest request)
    {
        var host = Header(request, "Host");
        if (host is null || !isAllowedHost(host))
        {
            return Error(421, "wrong host");
        }

        var path = request.Path;
        var query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            return Static(request, path);
        }

        if (Header(request, "Origin") is { } origin && !SameOrigin(origin, host))
        {
            return Error(403, "forbidden origin");
        }

        var isGet = request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase);
        if (!isGet && Header(request, "X-Remote") != "1")
        {
            return Error(403, "missing write header");
        }

        if (path == "/api/pair")
        {
            return request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? HandlePair(request) : Error(405, "method not allowed");
        }

        var device = registry.Authenticate(CookieValue(request));
        if (device is null)
        {
            return Error(401, "not paired");
        }

        if (!isGet)
        {
            return Error(405, "method not allowed");
        }

        return path switch
        {
            "/api/ping" => Json(200, JsonSerializer.Serialize(new Dictionary<string, string> { ["app"] = "Questkeeper", ["version"] = appVersion })),
            "/api/state" => Json(200, hub.CurrentJson),
            "/api/events" => new PhoneResponse(200, "text/event-stream", [], [Pair("Cache-Control", "no-store"), ..Common()], IsEventStream: true, DeviceId: device.Id),
            _ => Error(404, "not found"),
        };
    }

    private PhoneResponse HandlePair(PhoneRequest request)
    {
        PairBody? body = null;
        var parsed = request.Body.Length > 0 && PhoneFileJson.TryParsePair(request.Body, out body);
        var result = registry.TryPair(parsed ? body!.Token : null, parsed ? body!.Name : null);
        return result.Status switch
        {
            PairStatus.Paired => new PhoneResponse(204, "application/json", [], [
                Pair("Set-Cookie", $"{CookieName}={result.DeviceToken}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=31536000"),
                Pair("Cache-Control", "no-store"),
                ..Common()]),
            PairStatus.Locked => With(Error(429, "too many attempts"), Pair("Retry-After", "60")),
            _ => Error(401, "invalid pairing token"),
        };
    }

    private PhoneResponse Static(PhoneRequest request, string path)
    {
        var isHead = request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
        if (!isHead && !request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            return Error(405, "method not allowed");
        }

        var key = path == "/" ? "/index.html" : path;
        if (key.Contains("..", StringComparison.Ordinal) || key.Contains('\\') || assets.Find(key) is not { } asset)
        {
            return Error(404, "not found");
        }

        List<KeyValuePair<string, string>> headers =
        [
            Pair("ETag", asset.Etag), Pair("Cache-Control", "no-cache"), Pair("Content-Security-Policy", Csp), ..Common(),
        ];
        return Header(request, "If-None-Match") == asset.Etag
            ? new PhoneResponse(304, asset.ContentType, [], headers)
            : new PhoneResponse(200, asset.ContentType, isHead ? [] : asset.Bytes, headers);
    }

    private static PhoneResponse Json(int status, string json) =>
        new(status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), [Pair("Cache-Control", "no-store"), ..Common()]);

    private static PhoneResponse Error(int status, string message) => Json(status, $"{{\"error\":\"{message}\"}}");

    private static PhoneResponse With(PhoneResponse response, KeyValuePair<string, string> header) =>
        response with { Headers = [.. response.Headers, header] };

    private static KeyValuePair<string, string>[] Common() =>
        [Pair("X-Content-Type-Options", "nosniff"), Pair("Referrer-Policy", "no-referrer")];

    private static KeyValuePair<string, string> Pair(string name, string value) => new(name, value);

    private static bool SameOrigin(string origin, string host) =>
        string.Equals(origin, $"http://{host}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(origin, $"https://{host}", StringComparison.OrdinalIgnoreCase);

    private static string? CookieValue(PhoneRequest request)
    {
        if (Header(request, "Cookie") is not { } cookies)
        {
            return null;
        }

        foreach (var part in cookies.Split(';'))
        {
            var item = part.Trim();
            if (item.StartsWith(CookieName + "=", StringComparison.Ordinal))
            {
                return item[(CookieName.Length + 1)..];
            }
        }

        return null;
    }

    private static string? Header(PhoneRequest request, string name)
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
}
