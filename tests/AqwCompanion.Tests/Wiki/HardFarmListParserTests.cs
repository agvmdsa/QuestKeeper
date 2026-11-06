using System.Text;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.Wiki;

public class HardFarmListParserTests
{
    private readonly HardFarmListParser _parser = new();

    private static PageSnapshot Snapshot(string html, string url = "https://aqwwiki.wikidot.com/syfarm") =>
        PageSnapshot.Create(url, html, "1.0.0", DateTimeOffset.UnixEpoch);

    private static string Link(string slug) =>
        $"<a href=\"http://aqwwiki.wikidot.com/{slug}\">http://aqwwiki.wikidot.com/{slug}</a><br />\n";

    private static string Page(string body, string credit = "<p><em>Created by Tester, Revised by Checker</em></p>") =>
        $"<html><body><div id=\"nav\"><a href=\"http://aqwwiki.wikidot.com/menu-link\">x</a></div><div id=\"page-content\">\n{credit}\n{body}\n</div><div id=\"footer\"><a href=\"http://aqwwiki.wikidot.com/footer-link\">f</a></div></body></html>";

    private static string Section(string heading, int count, string prefix, string tag = "h1") =>
        $"<{tag}><span>{heading}</span></{tag}>\n" + string.Concat(Enumerable.Range(1, count).Select(i => Link($"{prefix}-{i}")));

    [Fact]
    public void Can_parse_only_the_syfarm_page_on_the_wiki()
    {
        Assert.True(_parser.CanParse(Snapshot("")));
        Assert.True(_parser.CanParse(Snapshot("", "https://aqwwiki.wikidot.com/SyFarm")));
        Assert.False(_parser.CanParse(Snapshot("", "https://aqwwiki.wikidot.com/classes")));
        Assert.False(_parser.CanParse(Snapshot("", "https://evil.example.com/syfarm")));
        Assert.False(_parser.CanParse(Snapshot("", "http://aqwwiki.wikidot.com/syfarm")));
    }

    [Fact]
    public void Reads_groups_entries_and_credit_in_page_order()
    {
        var result = _parser.Parse(Snapshot(Page(Section("Nation", 6, "nat") + Section("Legion", 5, "leg"))));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        var list = result.Data!;
        Assert.Equal(["Nation", "Legion"], list.Groups.Select(g => g.Name));
        Assert.Equal(["nat-1", "nat-2", "nat-3", "nat-4", "nat-5", "nat-6"], list.Groups[0].Slugs);
        Assert.Equal(5, list.Groups[1].Slugs.Count);
        Assert.Equal(11, list.Count);
        Assert.Equal(new HardFarmEntry("nat-1", "Nation"), list.Entries[0]);
        Assert.Equal(new HardFarmEntry("leg-1", "Legion"), list.Entries[6]);
        Assert.Equal("Created by Tester, Revised by Checker", list.Credit);
        Assert.Equal("headings-and-urls", result.Diagnostics.MatchedStrategy);
        Assert.Equal(11, result.Diagnostics.ItemCount);
        Assert.Empty(result.Diagnostics.MissingMarkers);
    }

    [Fact]
    public void Each_url_written_as_href_and_text_counts_once_and_text_only_urls_count_too()
    {
        var body = "<h1>Nation</h1>"
            + Link("a") + Link("b") + Link("a")
            + "<p>http://aqwwiki.wikidot.com/c<br />https://aqwwiki.wikidot.com/d</p>"
            + string.Concat(Enumerable.Range(1, 8).Select(i => Link($"x{i}")));

        var list = _parser.Parse(Snapshot(Page(body))).Data!;

        Assert.Equal(["a", "b", "c", "d", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8"], list.Groups[0].Slugs);
    }

    [Fact]
    public void A_link_whose_text_differs_from_its_href_uses_the_href()
    {
        var body = "<h1>Other</h1><a href=\"http://aqwwiki.wikidot.com/real-page\">Some title</a>" + Section("More", 10, "m");

        Assert.Equal("real-page", _parser.Parse(Snapshot(Page(body))).Data!.Groups[0].Slugs.Single());
    }

    [Fact]
    public void A_slug_in_two_groups_is_listed_in_both_but_counted_once_for_the_first_group()
    {
        var body = "<h1>One</h1>" + Link("shared") + Link("a1") + "<h1>Two</h1>" + Link("shared") + Link("b1")
            + Section("Pad", 10, "p");

        var list = _parser.Parse(Snapshot(Page(body))).Data!;

        Assert.Contains("shared", list.Groups[1].Slugs);
        Assert.Equal(1, list.Entries.Count(e => e.Slug == "shared"));
        Assert.Equal("One", list.Entries.Single(e => e.Slug == "shared").Group);
        Assert.Equal(13, list.Count);
    }

    [Fact]
    public void Urls_on_other_hosts_and_non_page_links_are_ignored()
    {
        var body = "<h1>Nation</h1>"
            + "<a href=\"https://evil.example.com/steal\">https://evil.example.com/steal</a>"
            + "<a href=\"https://aqwwiki.wikidot.com.evil.example/steal2\">https://aqwwiki.wikidot.com.evil.example/steal2</a>"
            + "<a href=\"/relative-page\">relative</a>"
            + "<a href=\"#top\">top</a><a href=\"http://aqwwiki.wikidot.com/a/b\">deep</a>"
            + "<a href=\"http://aqwwiki.wikidot.com/forum:thread\">forum</a>"
            + Section("Real", 10, "r").Replace("<h1><span>Real</span></h1>", string.Empty);

        var list = _parser.Parse(Snapshot(Page(body))).Data!;

        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"r-{i}"), list.Groups.Single().Slugs);
    }

    [Theory]
    [InlineData("h1")]
    [InlineData("h2")]
    [InlineData("h3")]
    [InlineData("h4")]
    [InlineData("h5")]
    public void Any_heading_level_starts_a_group(string tag)
    {
        var result = _parser.Parse(Snapshot(Page(Section("Daily Quest", 12, "q", tag))));

        Assert.Equal("Daily Quest", result.Data!.Groups.Single().Name);
    }

    [Fact]
    public void Entries_before_any_heading_go_to_an_ungrouped_group_and_same_named_headings_merge()
    {
        var body = Link("early") + Section("Other", 5, "o") + Section("Low Drop Rate", 2, "l") + Section("other", 5, "o2");

        var list = _parser.Parse(Snapshot(Page(body))).Data!;

        Assert.Equal([HardFarmListParser.UngroupedName, "Other", "Low Drop Rate"], list.Groups.Select(g => g.Name));
        Assert.Equal(10, list.Groups[1].Slugs.Count);
    }

    [Fact]
    public void A_heading_without_entries_makes_no_group()
    {
        var list = _parser.Parse(Snapshot(Page("<h1>Empty</h1>" + Section("Full", 10, "f")))).Data!;

        Assert.Equal(["Full"], list.Groups.Select(g => g.Name));
    }

    [Fact]
    public void Links_outside_the_page_content_are_ignored()
    {
        var list = _parser.Parse(Snapshot(Page(Section("Nation", 10, "n")))).Data!;

        Assert.DoesNotContain(list.Entries, e => e.Slug is "menu-link" or "footer-link");
    }

    [Fact]
    public void The_credit_is_capped_cleaned_and_optional()
    {
        var long_ = "<p><em>Created by " + new string('x', 300) + "</em></p>";
        Assert.Equal(120, _parser.Parse(Snapshot(Page(Section("N", 10, "n"), long_))).Data!.Credit!.Length);

        Assert.Null(_parser.Parse(Snapshot(Page(Section("N", 10, "n"), "<p>Nothing here</p>"))).Data!.Credit);
    }

    [Fact]
    public void A_short_list_is_suspicious_but_keeps_its_data()
    {
        var result = _parser.Parse(Snapshot(Page(Section("Nation", 9, "n"))));

        Assert.Equal(ParseOutcome.Suspicious, result.Outcome);
        Assert.Equal(9, result.Data!.Count);
        Assert.Equal(9, result.Diagnostics.ItemCount);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Exactly_the_minimum_is_ok()
    {
        Assert.Equal(ParseOutcome.Ok, _parser.Parse(Snapshot(Page(Section("Nation", 10, "n")))).Outcome);
    }

    [Theory]
    [InlineData("<html><body><div id=\"page-content\"><h1>Nation</h1><p>No links</p></div></body></html>")]
    [InlineData("<html><body><p>Nothing</p></body></html>")]
    [InlineData("")]
    [InlineData("not html at all \u0000 <<<")]
    public void A_page_without_entries_or_content_fails(string html)
    {
        var result = _parser.Parse(Snapshot(html));

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
        Assert.NotEmpty(result.Diagnostics.MissingMarkers);
        Assert.Equal(0, result.Diagnostics.ItemCount);
    }

    [Fact]
    public void Does_not_leak_page_text_into_the_diagnostics()
    {
        var result = _parser.Parse(Snapshot("<div id=\"page-content\">SECRET-TEXT</div>"));

        Assert.DoesNotContain("SECRET-TEXT", result.Reason);
        Assert.DoesNotContain("SECRET-TEXT", string.Join(' ', result.Diagnostics.MissingMarkers));
    }

    [Fact]
    public void A_large_list_parses()
    {
        var sb = new StringBuilder();
        for (var g = 0; g < 10; g++)
        {
            sb.Append(Section("Group " + g, 300, "g" + g));
        }

        var result = _parser.Parse(Snapshot(Page(sb.ToString())));

        Assert.Equal(3000, result.Data!.Count);
    }

    public static TheoryData<string> HostileSlugs => new()
    {
        "a%3Fb", "a%23b", "a%25b", "a%0D%0Ab", "..", "a/b", "a:b", "a%3Ab", "a%40b", "a%20b", "caf%C3%A9", "%D0%B0bc", "a.b", "-a", "a%2Fb", "a%2E%2E", new string('a', 101),
    };

    [Theory]
    [MemberData(nameof(HostileSlugs))]
    public void Entries_with_an_invalid_slug_are_skipped_whether_linked_or_written_as_text(string hostile)
    {
        var body = Section("Nation", 10, "ok")
            + $"<a href=\"http://aqwwiki.wikidot.com/{hostile}\">x</a>\n"
            + $"<p>http://aqwwiki.wikidot.com/{hostile}</p>\n"
            + Link(hostile);

        var result = _parser.Parse(Snapshot(Page(body)));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"ok-{i}"), result.Data!.Groups.Single().Slugs);
        Assert.Equal(10, result.Data.Count);
    }

    [Fact]
    public void Upper_case_slugs_are_normalised_and_kept()
    {
        var result = _parser.Parse(Snapshot(Page(Section("Nation", 10, "ok") + Link("Void-Highlord"))));

        Assert.Contains("void-highlord", result.Data!.Groups.Single().Slugs);
    }
}

