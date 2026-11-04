namespace AqwCompanion.Core.Ingestion;

public sealed record PageSnapshot
{
    public required string Url { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required string Payload { get; init; }
    public required string AppVersion { get; init; }

    public static PageSnapshot Create(string rawUrl, string payload, string appVersion, DateTimeOffset capturedAt)
    {
        var uri = new Uri(rawUrl, UriKind.Absolute);
        // On Unix, "/path" parses as an absolute file:// URI, so require a web scheme explicitly.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new UriFormatException($"Expected an absolute http(s) URL but got scheme '{uri.Scheme}'.");
        }

        return new PageSnapshot
        {
            Url = uri.GetLeftPart(UriPartial.Path),
            CapturedAt = capturedAt,
            Payload = payload,
            AppVersion = appVersion,
        };
    }
}
