using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Core.Wiki;

/// <summary>
/// Reads a wiki type index page (<c>/classes</c>, <c>/armors</c> ...): entries are <c>&lt;a href="/slug"&gt;Name&lt;/a&gt;</c>
/// inside <c>#page-content</c>. In-page anchors, <c>system:</c>/<c>forum:</c> links and the sort-by menu are ignored; names
/// are cleaned. Parsing never throws and nothing parsed is logged.
/// </summary>
public sealed class WikiIndexParser : IPageParser<WikiIndex>
{
    public const string ContentMarker = "#page-content";
    public const string EntryMarker = "#page-content a[href^='/']";

    private const string ListStrategy = "list-pages-items";
    private const string AnchorStrategy = "content-anchors";
    private const int MaxNameLength = 200;

    public string Name => "wiki-type-index";
    public string Version => "1";

    public bool CanParse(PageSnapshot snapshot) =>
        WikiUrls.IsWikiPage(snapshot.Url) && WikiUrls.Slug(snapshot.Url) is { } slug && WikiUrls.IsTypeIndex(slug);

    public ParseResult<WikiIndex> Parse(PageSnapshot snapshot)
    {
        var indexSlug = WikiUrls.Slug(snapshot.Url) ?? string.Empty;
        var document = new HtmlParser().ParseDocument(snapshot.Payload);
        var content = document.QuerySelector(ContentMarker);
        if (content is null)
        {
            return Failed("the page content was not found", ContentMarker);
        }

        // The real pages list entries inside .list-pages-item boxes; the sort-by menu above them is not an entry.
        var strategy = ListStrategy;
        var anchors = content.QuerySelectorAll(".list-pages-item a[href]").ToList();
        if (anchors.Count == 0)
        {
            strategy = AnchorStrategy;
            anchors = content.QuerySelectorAll("a[href]").ToList();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<WikiIndexEntry>();
        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttribute("href");
            if (href is null || href.StartsWith('#'))
            {
                continue;
            }

            var slug = WikiUrls.Slug(href);
            var name = TextCleaner.Clean(anchor.TextContent, MaxNameLength);
            if (!WikiUrls.IsValidSlug(slug) || name.Length == 0 || IsNavigation(name) || !seen.Add(slug))
            {
                continue;
            }

            entries.Add(new WikiIndexEntry(slug, name));
        }

        if (entries.Count == 0)
        {
            return Failed("no entries were found", EntryMarker);
        }

        return ParseResult<WikiIndex>.Ok(
            new WikiIndex(indexSlug, WikiTypes.ForIndex(indexSlug), entries),
            new ParseDiagnostics(Name, Version, strategy, [], entries.Count));
    }

    private static bool IsNavigation(string name) =>
        name.EqualsIgnoreCase("Back to Top") || name.EqualsIgnoreCase("Go to");

    private ParseResult<WikiIndex> Failed(string reason, string marker) =>
        ParseResult<WikiIndex>.Failed(reason, new ParseDiagnostics(Name, Version, null, [marker], 0));
}
