using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Site;

namespace AqwCompanion.Core.Account;

/// <summary>
/// Reads the character name from the account home page, plus three allow-listed summary fields
/// (AdventureCoins, account type with its membership line, Gears of Doom). Every other account field
/// (e-mail, dates, age group...) is never read. Nothing parsed is logged.
/// </summary>
public sealed class AccountHomeParser : IPageParser<AccountIdentity>
{
    public const string CharacterPageLinkMarker = "a[href*='/CharPage?id=']";
    public const string UsernameRowMarker = ".tblHeader(Account Info) .row > .fw-bold(Username:) + div";

    private const string LinkStrategy = "character-page-link";
    private const string RowStrategy = "username-row";
    private const string AdventureCoinsLabel = "AdventureCoins:";
    private const string AccountTypeLabel = "Account Type:";
    private const string GearsOfDoomLabel = "Doom Wheel Gears:";
    private const int MaxAccountTypeLength = 50;
    private const int MaxMembershipLength = 200;
    private static readonly Regex GearsPattern = new(
        @"You have\s+(\d[\d,]*)\s+Gears? of Doom", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Uri RootUri = new(AqwUrls.Root);

    public string Name => "account-home";
    public string Version => "2";

    public bool CanParse(PageSnapshot snapshot) => AqwUrls.IsAccountHome(snapshot.Url);

    public ParseResult<AccountIdentity> Parse(PageSnapshot snapshot)
    {
        var document = new HtmlParser().ParseDocument(snapshot.Payload);

        if (FromCharacterPageLink(document) is { } linked)
        {
            return Ok(linked, LinkStrategy, FromSummaryRows(document));
        }

        if (FromUsernameRow(document) is { } username)
        {
            return Ok(username, RowStrategy, FromSummaryRows(document));
        }

        return ParseResult<AccountIdentity>.Failed(
            "character name not found",
            new ParseDiagnostics(Name, Version, null, [CharacterPageLinkMarker, UsernameRowMarker], 0));
    }

    private ParseResult<AccountIdentity> Ok(string characterName, string strategy, AccountSummary? summary) =>
        ParseResult<AccountIdentity>.Ok(
            new AccountIdentity(characterName, summary),
            new ParseDiagnostics(Name, Version, strategy, [], 1));

    private static string? FromCharacterPageLink(IDocument document)
    {
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            if (Uri.TryCreate(RootUri, anchor.GetAttribute("href"), out var uri)
                && AqwUrls.IsAccountPage(uri, "/CharPage")
                && TextCleaner.NonBlank(QueryValue(uri.Query, "id")) is { } name)
            {
                return name;
            }
        }

        return null;
    }

    private static string? FromUsernameRow(IDocument document)
    {
        var label = FindAccountInfoCard(document)?.QuerySelectorAll(".fw-bold")
            .FirstOrDefault(cell => cell.TextContent.Trim().EqualsIgnoreCase("Username:"));

        return TextCleaner.NonBlank(label?.NextElementSibling?.TextContent);
    }

    private static IElement? FindAccountInfoCard(IDocument document) =>
        document.QuerySelectorAll(".tblHeader")
            .FirstOrDefault(header => header.TextContent.Trim().EqualsIgnoreCase("Account Info"))
            ?.ParentElement;

    /// <summary>
    /// Reads only the three allow-listed rows of the Account Info card; any other row is skipped without being
    /// looked at. A field that is missing or unparseable is null, and no field at all gives no summary.
    /// </summary>
    private static AccountSummary? FromSummaryRows(IDocument document)
    {
        var card = FindAccountInfoCard(document);
        if (card is null)
        {
            return null;
        }

        var coins = ValueCell(card, AdventureCoinsLabel);
        var type = ValueCell(card, AccountTypeLabel);
        var gears = ValueCell(card, GearsOfDoomLabel);

        var spans = type?.QuerySelectorAll("span").ToList();
        var summary = new AccountSummary(
            ParseCount(coins?.TextContent),
            Clean(spans?.ElementAtOrDefault(0)?.TextContent, MaxAccountTypeLength),
            Clean(spans?.ElementAtOrDefault(1)?.TextContent, MaxMembershipLength),
            ParseGears(gears?.TextContent));

        return summary == new AccountSummary(null, null, null, null) ? null : summary;
    }

    private static IElement? ValueCell(IElement card, string label) =>
        card.QuerySelectorAll(".row > .fw-bold")
            .FirstOrDefault(cell => cell.TextContent.Trim().EqualsIgnoreCase(label))
            ?.NextElementSibling;

    private static int? ParseCount(string? text) =>
        NumberParsing.TryParseNonNegativeInt(text, NumberStyles.AllowThousands, out var value)
            ? value
            : null;

    private static int? ParseGears(string? text)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            var match = GearsPattern.Match(text);
            return match.Success ? ParseCount(match.Groups[1].Value) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>The text without control characters, trimmed and capped; null when blank.</summary>
    private static string? Clean(string? value, int maxLength) =>
        TextCleaner.NonBlank(TextCleaner.Clean(value, maxLength));

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && pair[..separator].EqualsIgnoreCase(key))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }
}
