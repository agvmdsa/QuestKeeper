using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Core.Wiki;

/// <summary>
/// Reads the community Hard Farming list (<c>/syfarm</c>): a "Created by ..., Revised by ..." line, then headings, each
/// followed by wiki page URLs written as links and/or plain text. Only URLs on the wiki host count. Parsing never
/// throws and nothing parsed is logged.
/// </summary>
public sealed class HardFarmListParser : IPageParser<HardFarmList>
{
    public const int MinEntries = 10;
    public const string ContentMarker = "#page-content";
    public const string HeadingMarker = "h1-h6 headings in #page-content";
    public const string UngroupedName = "Ungrouped";

    private const string Strategy = "headings-and-urls";
    private const int MaxCreditLength = 120;
    private const int MaxGroupNameLength = 100;

    private static readonly Regex UrlPattern = new(
        @"https?://aqwwiki\.wikidot\.com/[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public string Name => "hard-farm-list";
    public string Version => "1";

    public bool CanParse(PageSnapshot snapshot) =>
        WikiUrls.IsWikiPage(snapshot.Url) && WikiUrls.Slug(snapshot.Url) == WikiUrls.HardFarmSlug;

    public ParseResult<HardFarmList> Parse(PageSnapshot snapshot)
    {
        var document = new HtmlParser().ParseDocument(snapshot.Payload);
        var content = document.QuerySelector(ContentMarker);
        if (content is null)
        {
            return ParseResult<HardFarmList>.Failed(
                "the page content was not found", new ParseDiagnostics(Name, Version, null, [ContentMarker], 0));
        }

        var groups = new List<(string Name, List<string> Slugs)>();
        string? credit = null;
        var current = (List<string>?)null;
        Walk(content);

        var result = new HardFarmList(
            groups.Where(g => g.Slugs.Count > 0).Select(g => new HardFarmGroup(g.Name, g.Slugs)).ToList(),
            credit);

        var missing = new List<string>();
        if (result.Groups.Count == 0)
        {
            missing.Add(HeadingMarker);
        }

        var diagnostics = new ParseDiagnostics(Name, Version, Strategy, missing, result.Count);
        if (result.Count == 0)
        {
            return ParseResult<HardFarmList>.Failed("no wiki page links were found", diagnostics with { MatchedStrategy = null });
        }

        return result.Groups.Count >= 1 && result.Count >= MinEntries
            ? ParseResult<HardFarmList>.Ok(result, diagnostics)
            : ParseResult<HardFarmList>.Suspicious(result, "the list is much shorter than expected", diagnostics);

        void Walk(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                switch (child)
                {
                    case IElement { LocalName: "a" } anchor:
                        Add(AbsoluteSlug(anchor.GetAttribute("href")) ?? SlugOfText(anchor.TextContent));
                        break;
                    case IElement element when IsHeading(element):
                        StartGroup(element.TextContent);
                        break;
                    case IElement element when element.LocalName is "script" or "style":
                        break;
                    case IElement element:
                        if (current is null && credit is null && element.LocalName is "p" or "em" or "div"
                            && element.TextContent.Contains("Created by", StringComparison.OrdinalIgnoreCase)
                            && element.TextContent.Length < 400)
                        {
                            credit = TextCleaner.NonBlank(TextCleaner.Clean(element.TextContent, MaxCreditLength));
                        }

                        Walk(element);
                        break;
                    case IText text:
                        foreach (var slug in SlugsInText(text.Data))
                        {
                            Add(slug);
                        }

                        break;
                }
            }
        }

        void StartGroup(string heading)
        {
            var name = TextCleaner.Clean(heading, MaxGroupNameLength);
            if (name.Length == 0)
            {
                return;
            }

            var existing = groups.FindIndex(g => g.Name.EqualsIgnoreCase(name));
            if (existing < 0)
            {
                groups.Add((name, []));
                existing = groups.Count - 1;
            }

            current = groups[existing].Slugs;
        }

        void Add(string? slug)
        {
            if (!WikiUrls.IsValidSlug(slug))
            {
                return;
            }

            if (current is null)
            {
                StartGroup(UngroupedName);
            }

            var target = current!;
            if (!target.Contains(slug, StringComparer.Ordinal))
            {
                target.Add(slug);
            }
        }
    }

    private static bool IsHeading(IElement element) =>
        element.LocalName.Length == 2 && element.LocalName[0] == 'h' && element.LocalName[1] is >= '1' and <= '6';

    /// <summary>Only absolute wiki URLs count on this page; a relative link is not a list entry.</summary>
    private static string? AbsoluteSlug(string? href) =>
        href is not null && href.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase) ? WikiUrls.Slug(href) : null;

    private static string? SlugOfText(string text) => SlugsInText(text).FirstOrDefault();

    private static IEnumerable<string> SlugsInText(string text)
    {
        MatchCollection matches;
        try
        {
            matches = UrlPattern.Matches(text);
        }
        catch (RegexMatchTimeoutException)
        {
            yield break;
        }

        foreach (Match match in matches)
        {
            if (WikiUrls.Slug(match.Value) is { } slug)
            {
                yield return slug;
            }
        }
    }
}
