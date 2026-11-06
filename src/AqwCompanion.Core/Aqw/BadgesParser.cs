using System.Text.Json;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Core.Aqw;

/// <summary>
/// Reads the public badges list (a JSON array of objects with title, description and category).
/// The badge image name is ignored. Parsing never throws and nothing parsed is logged.
/// </summary>
public sealed class BadgesParser : IPageParser<CharacterBadges>
{
    public const string ArrayMarker = "json array of badges";
    public const string TitleMarker = "sTitle";

    private const string ArrayStrategy = "json-array";
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 500;
    private const int MaxCategoryLength = 100;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 8 };

    public string Name => "character-badges";
    public string Version => "1";

    public bool CanParse(PageSnapshot snapshot) => AqwUrls.IsBadges(snapshot.Url);

    public ParseResult<CharacterBadges> Parse(PageSnapshot snapshot)
    {
        var body = snapshot.Payload.Trim();
        if (body.Length == 0 || string.Equals(body, "error", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("the badges endpoint reported an error", [ArrayMarker]);
        }

        try
        {
            using var document = JsonDocument.Parse(body, Options);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Failed("the badges response is not a list", [ArrayMarker]);
            }

            var badges = new List<Badge>();
            var entries = 0;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                entries++;
                if (ReadBadge(entry) is { } badge)
                {
                    badges.Add(badge);
                }
            }

            var diagnostics = new ParseDiagnostics(Name, Version, ArrayStrategy, [], badges.Count);
            return entries > 0 && badges.Count == 0
                ? ParseResult<CharacterBadges>.Suspicious(
                    new CharacterBadges(badges), "no badge entry had a title", diagnostics with { MissingMarkers = [TitleMarker] })
                : ParseResult<CharacterBadges>.Ok(new CharacterBadges(badges), diagnostics);
        }
        catch (JsonException)
        {
            return Failed("the badges response is not valid JSON", [ArrayMarker]);
        }
    }

    private ParseResult<CharacterBadges> Failed(string reason, IReadOnlyList<string> missing) =>
        ParseResult<CharacterBadges>.Failed(reason, new ParseDiagnostics(Name, Version, null, missing, 0));

    private static Badge? ReadBadge(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = ReadText(entry, "sTitle", MaxTitleLength);
        if (title is null)
        {
            return null;
        }

        return new Badge(
            title,
            ReadText(entry, "sDesc", MaxDescriptionLength) ?? string.Empty,
            ReadText(entry, "sCategory", MaxCategoryLength) ?? CharacterBadges.UncategorizedName);
    }

    /// <summary>The cleaned string value, or null when the property is missing, not a string or blank.</summary>
    private static string? ReadText(JsonElement entry, string property, int maxLength)
    {
        if (!entry.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = new string((value.GetString() ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > maxLength ? text[..maxLength] : text;
    }
}
