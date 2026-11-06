using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.Wiki;

public class WikiIndexParserTests
{
    private readonly WikiIndexParser _parser = new();

    private static PageSnapshot Snapshot(string html, string url = "https://aqwwiki.wikidot.com/classes") =>
        PageSnapshot.Create(url, html, "1.0.0", DateTimeOffset.UnixEpoch);

    private const string Nav =
        "<p><sub><a name=\"top\"></a> Go to <a href=\"#Numbers\">#</a>, <a href=\"#A\">A</a>, <a href=\"#B\">B</a></sub></p>";

    private static string Item(string slug, string name) =>
        $"<div class=\"list-pages-item\">\n<p><a href=\"/{slug}\">{name}</a></p>\n<img src=\"https://aqwwiki.wdfiles.com/x.png\" alt=\"x.png\" /></div>\n";

    private static string Section(string letter, string items) =>
        $"<p><a name=\"{letter}\"></a> <strong>{letter}</strong></p>\n<div class=\"list-pages-box\">{items}</div>\n<div style=\"text-align: right;\"><p><sub><a href=\"#top\">Back to Top</a></sub></p></div>\n";

    private static string Page(string body) =>
        $"<html><body><a href=\"/menu\">Menu</a><div id=\"page-content\">{body}</div><a href=\"/footer\">Footer</a></body></html>";

    [Fact]
    public void Can_parse_only_the_type_indexes_on_the_wiki()
    {
        Assert.True(_parser.CanParse(Snapshot("")));
        Assert.True(_parser.CanParse(Snapshot("", "https://aqwwiki.wikidot.com/helmets-hoods")));
        Assert.False(_parser.CanParse(Snapshot("", "https://aqwwiki.wikidot.com/syfarm")));
        Assert.False(_parser.CanParse(Snapshot("", "https://evil.example.com/classes")));
    }

    [Fact]
    public void Reads_slug_and_name_pairs_in_page_order_and_skips_the_navigation()
    {
        var body = Nav + Section("A", Item("abyssal-angel-legend", "Abyssal Angel (Legend)") + Item("alpha-class", "Alpha &amp; Omega"))
            + Section("B", Item("berserker", "  Berserker\n"));

        var result = _parser.Parse(Snapshot(Page(body)));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        var index = result.Data!;
        Assert.Equal("classes", index.IndexSlug);
        Assert.Equal(WikiType.Class, index.Type);
        Assert.Equal(
            [new WikiIndexEntry("abyssal-angel-legend", "Abyssal Angel (Legend)"), new WikiIndexEntry("alpha-class", "Alpha & Omega"), new WikiIndexEntry("berserker", "Berserker")],
            index.Entries);
        Assert.Equal(3, result.Diagnostics.ItemCount);
        Assert.Equal("list-pages-items", result.Diagnostics.MatchedStrategy);
    }

    [Fact]
    public void The_type_follows_the_index_page()
    {
        var html = Page(Section("A", Item("x", "X")));

        Assert.Equal(WikiType.Pet, _parser.Parse(Snapshot(html, "https://aqwwiki.wikidot.com/battle-pets")).Data!.Type);
        Assert.Equal(WikiType.Other, _parser.Parse(Snapshot(html, "https://aqwwiki.wikidot.com/necklaces")).Data!.Type);
    }

    [Fact]
    public void The_sort_by_menu_above_the_lists_is_not_an_entry()
    {
        var menu = "<p><sub>Sort by: <a href=\"/sort-by-rare-classes\">Rare</a> - <a href=\"http://aqwwiki.wikidot.com/classes-with-no-rank-10-passive\">No Rank 10</a></sub></p>";

        var index = _parser.Parse(Snapshot(Page(menu + Nav + Section("A", Item("a1", "A1"))))).Data!;

        Assert.Equal(["a1"], index.Entries.Select(e => e.Slug));
    }

    [Fact]
    public void Without_list_boxes_all_content_anchors_are_used_but_anchors_and_special_links_are_not()
    {
        var body = Nav
            + "<ul><li><a href=\"/axes\">Axes</a></li><li><a href=\"/system:recent-changes\">Recent</a></li>"
            + "<li><a href=\"/forum:start\">Forum</a></li><li><a href=\"#top\">Back to Top</a></li>"
            + "<li><a href=\"https://evil.example.com/x\">Evil</a></li><li><a href=\"/a/b\">Deep</a></li>"
            + "<li><a href=\"/bows\"><img src=\"x.png\" /></a></li><li><a href=\"/back\">Back to Top</a></li>"
            + "<li><a href=\"http://aqwwiki.wikidot.com/swords\">Swords</a></li></ul>";

        var result = _parser.Parse(Snapshot(Page(body), "https://aqwwiki.wikidot.com/weapons"));

        Assert.Equal(["axes", "swords"], result.Data!.Entries.Select(e => e.Slug));
        Assert.Equal("content-anchors", result.Diagnostics.MatchedStrategy);
    }

    [Fact]
    public void Duplicate_slugs_keep_the_first_name_and_slugs_are_lower_cased()
    {
        var index = _parser.Parse(Snapshot(Page(Section("A", Item("Dup-Item", "First") + Item("dup-item", "Second"))))).Data!;

        Assert.Equal(new WikiIndexEntry("dup-item", "First"), Assert.Single(index.Entries));
    }

    [Fact]
    public void Names_are_cleaned_and_capped()
    {
        var index = _parser.Parse(Snapshot(Page(Section("A", Item("long", new string('n', 500)) + Item("ctl", "Ab\u0007c"))))).Data!;

        Assert.Equal(200, index.Entries[0].Name.Length);
        Assert.Equal("Abc", index.Entries[1].Name);
    }

    [Theory]
    [InlineData("<html><body><div id=\"page-content\"><p>Nothing listed</p></div></body></html>")]
    [InlineData("<html><body><a href=\"/menu\">Menu</a></body></html>")]
    [InlineData("<div id=\"page-content\"><a href=\"#A\">A</a></div>")]
    [InlineData("")]
    public void A_page_without_entries_fails(string html)
    {
        var result = _parser.Parse(Snapshot(html));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
        Assert.NotEmpty(result.Diagnostics.MissingMarkers);
    }

    [Fact]
    public void A_large_index_parses()
    {
        var items = string.Concat(Enumerable.Range(0, 3000).Select(i => Item($"item-{i}", $"Item {i}")));

        var result = _parser.Parse(Snapshot(Page(Section("A", items))));

        Assert.Equal(3000, result.Data!.Entries.Count);
    }

    public static TheoryData<string> HostileSlugs => new()
    {
        "a%3Fb", "a%23b", "a%25b", "a%0D%0Ab", "..", "a/b", "a:b", "a%3Ab", "a%40b", "a%20b", "caf%C3%A9", "%D0%B0bc", "a.b", "-a", "a%2Fb", "a%2E%2E", new string('a', 101),
    };

    [Theory]
    [MemberData(nameof(HostileSlugs))]
    public void Entries_with_an_invalid_slug_are_skipped(string hostile)
    {
        var items = Item("good-one", "Good One") + Item(hostile, "Hostile") + Item("good-two", "Good Two");

        var result = _parser.Parse(Snapshot(Page(Section("A", items))));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(["good-one", "good-two"], result.Data!.Entries.Select(e => e.Slug));
        Assert.Equal(2, result.Diagnostics.ItemCount);
    }

    [Fact]
    public void An_index_whose_only_entries_are_invalid_fails()
    {
        var result = _parser.Parse(Snapshot(Page(Section("A", Item("a%3Fb", "Bad") + Item("a/b", "Deep")))));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
    }
}

