using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Core.Aqw;

/// <summary>
/// Reads only the character name from the account home page. Other account fields (e-mail, dates,
/// age group...) are never read. Nothing parsed is logged.
/// </summary>
public sealed class AccountHomeParser : IPageParser<AccountIdentity>
{
    public const string CharacterPageLinkMarker = "a[href*='/CharPage?id=']";
    public const string UsernameRowMarker = ".tblHeader(Account Info) .row > .fw-bold(Username:) + div";

    private const string LinkStrategy = "character-page-link";
    private const string RowStrategy = "username-row";
    private static readonly Uri RootUri = new(AqwUrls.Root);

    public string Name => "account-home";
    public string Version => "1";

    public bool CanParse(PageSnapshot snapshot) => AqwUrls.IsAccountHome(snapshot.Url);

    public ParseResult<AccountIdentity> Parse(PageSnapshot snapshot)
    {
        var document = new HtmlParser().ParseDocument(snapshot.Payload);

        if (FromCharacterPageLink(document) is { } linked)
        {
            return Ok(linked, LinkStrategy);
        }

        if (FromUsernameRow(document) is { } username)
        {
            return Ok(username, RowStrategy);
        }

        return ParseResult<AccountIdentity>.Failed(
            "character name not found",
            new ParseDiagnostics(Name, Version, null, [CharacterPageLinkMarker, UsernameRowMarker], 0));
    }

    private ParseResult<AccountIdentity> Ok(string characterName, string strategy) =>
        ParseResult<AccountIdentity>.Ok(
            new AccountIdentity(characterName),
            new ParseDiagnostics(Name, Version, strategy, [], 1));

    private static string? FromCharacterPageLink(IDocument document)
    {
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            if (Uri.TryCreate(RootUri, anchor.GetAttribute("href"), out var uri)
                && AqwUrls.IsAccountPage(uri, "/CharPage")
                && NonBlank(QueryValue(uri.Query, "id")) is { } name)
            {
                return name;
            }
        }

        return null;
    }

    private static string? FromUsernameRow(IDocument document)
    {
        var card = document.QuerySelectorAll(".tblHeader")
            .FirstOrDefault(header => string.Equals(header.TextContent.Trim(), "Account Info", StringComparison.OrdinalIgnoreCase))
            ?.ParentElement;

        var label = card?.QuerySelectorAll(".fw-bold")
            .FirstOrDefault(cell => string.Equals(cell.TextContent.Trim(), "Username:", StringComparison.OrdinalIgnoreCase));

        return NonBlank(label?.NextElementSibling?.TextContent);
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && string.Equals(pair[..separator], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }

    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
