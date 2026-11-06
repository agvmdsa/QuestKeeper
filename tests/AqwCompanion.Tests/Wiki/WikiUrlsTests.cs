using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.Wiki;

public class WikiUrlsTests
{
    [Theory]
    [InlineData("https://aqwwiki.wikidot.com/syfarm", "syfarm")]
    [InlineData("http://aqwwiki.wikidot.com/Void-Highlord-Class", "void-highlord-class")]
    [InlineData("HTTPS://AQWWIKI.WIKIDOT.COM/syfarm/", "syfarm")]
    [InlineData("/classes", "classes")]
    [InlineData("/classes#top", "classes")]
    [InlineData("https://aqwwiki.wikidot.com/pets?x=1", "pets")]
    [InlineData("https://aqwwiki.wikidot.com/system:page-tags", "system:page-tags")]
    public void Slug_is_the_lower_case_path_segment(string url, string expected) =>
        Assert.Equal(expected, WikiUrls.Slug(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("#top")]
    [InlineData("https://aqwwiki.wikidot.com/")]
    [InlineData("https://aqwwiki.wikidot.com/a/b")]
    [InlineData("/a/b")]
    [InlineData("//evil.example/a")]
    [InlineData("https://evil.example/syfarm")]
    [InlineData("https://aqwwiki.wikidot.com.evil.example/syfarm")]
    [InlineData("https://user@aqwwiki.wikidot.com/syfarm")]
    [InlineData("ftp://aqwwiki.wikidot.com/syfarm")]
    [InlineData("syfarm")]
    public void Slug_is_null_for_anything_else(string? url) => Assert.Null(WikiUrls.Slug(url));

    [Fact]
    public void Page_builds_the_https_address()
    {
        Assert.Equal("https://aqwwiki.wikidot.com/syfarm", WikiUrls.Page("syfarm"));
        Assert.Equal("https://aqwwiki.wikidot.com/syfarm", WikiUrls.HardFarm);
        Assert.Equal("https://aqwwiki.wikidot.com/robots.txt", WikiUrls.Robots);
    }

    [Theory]
    [InlineData("https://aqwwiki.wikidot.com/syfarm", true)]
    [InlineData("https://AQWWIKI.wikidot.com/x", true)]
    [InlineData("http://aqwwiki.wikidot.com/syfarm", false)]
    [InlineData("https://aqwwiki.wikidot.com:8443/syfarm", false)]
    [InlineData("https://aqwwiki.wikidot.com.evil.example/syfarm", false)]
    [InlineData("https://account.aq.com/Home", false)]
    [InlineData("/syfarm", false)]
    [InlineData(null, false)]
    public void IsWikiPage_accepts_only_https_on_the_wiki_host(string? url, bool expected) =>
        Assert.Equal(expected, WikiUrls.IsWikiPage(url));

    [Fact]
    public void The_type_indexes_are_the_fifteen_pages_with_classes_first()
    {
        Assert.Equal(15, WikiUrls.TypeIndexSlugs.Count);
        Assert.Equal("classes", WikiUrls.TypeIndexSlugs[0]);
        Assert.Equal(WikiUrls.TypeIndexSlugs.Count, WikiUrls.TypeIndexSlugs.Distinct().Count());
        Assert.True(WikiUrls.IsTypeIndex("necklaces"));
        Assert.False(WikiUrls.IsTypeIndex("syfarm"));
    }

    public static TheoryData<string?> HostileSlugs => new()
    {
        "a%3Fb", "a%23b", "a%25b", "a%0D%0Ab", "..", ".", "a/b", "a:b", "a@b", "a b", "a?b", "a#b", "a%b", "a.b", "-a", "_a",
        "caf\u00e9", "\u0430bc", "A", "a\nb", "a\r\nb", "a\0b", "", " ", null, new string('a', 101),
    };

    [Theory]
    [MemberData(nameof(HostileSlugs))]
    public void Hostile_slugs_are_not_valid_and_have_no_address(string? slug)
    {
        Assert.False(WikiUrls.IsValidSlug(slug));
        Assert.False(WikiUrls.TryPage(slug, out var url));
        Assert.Equal(string.Empty, url);
        Assert.Throws<ArgumentException>(() => WikiUrls.Page(slug!));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("void-highlord-class")]
    [InlineData("a_b-c9")]
    public void Plain_slugs_are_valid(string slug)
    {
        Assert.True(WikiUrls.IsValidSlug(slug));
        Assert.True(WikiUrls.TryPage(slug, out var url));
        Assert.Equal("https://aqwwiki.wikidot.com/" + slug, url);
        Assert.Equal(url, WikiUrls.Page(slug));
    }

    [Fact]
    public void A_slug_of_exactly_one_hundred_characters_is_valid()
    {
        Assert.True(WikiUrls.IsValidSlug(new string('a', 100)));
        Assert.False(WikiUrls.IsValidSlug(new string('a', 101)));
    }

    [Theory]
    [InlineData("https://aqwwiki.wikidot.com/syfarm", true)]
    [InlineData("https://aqwwiki.wikidot.com/void-highlord-class", true)]
    [InlineData("https://aqwwiki.wikidot.com/", false)]
    [InlineData("https://aqwwiki.wikidot.com/a?b", false)]
    [InlineData("https://aqwwiki.wikidot.com/a#b", false)]
    [InlineData("https://aqwwiki.wikidot.com/a%3Fb", false)]
    [InlineData("https://aqwwiki.wikidot.com/a/b", false)]
    [InlineData("https://aqwwiki.wikidot.com/a\r\nb", false)]
    [InlineData("https://aqwwiki.wikidot.com/a b", false)]
    [InlineData("https://aqwwiki.wikidot.com/A", false)]
    [InlineData("https://aqwwiki.wikidot.com/robots.txt", false)]
    [InlineData("http://aqwwiki.wikidot.com/syfarm", false)]
    [InlineData("HTTPS://aqwwiki.wikidot.com/syfarm", false)]
    [InlineData("https://AQWWIKI.wikidot.com/syfarm", false)]
    [InlineData("https://aqwwiki.wikidot.com:8443/syfarm", false)]
    [InlineData("https://aqwwiki.wikidot.com@evil.example/syfarm", false)]
    [InlineData("https://aqwwiki.wikidot.com/syfarm\n", false)]
    [InlineData("https://aqwwiki.wikidot.com/syfarm ", false)]
    [InlineData(" https://aqwwiki.wikidot.com/syfarm", false)]
    [InlineData("https://aqwwiki.wikidot.com.evil.example/syfarm", false)]
    [InlineData(null, false)]
    public void IsSafePageUrl_accepts_only_the_exact_shape_the_page_builder_makes(string? url, bool expected) =>
        Assert.Equal(expected, WikiUrls.IsSafePageUrl(url));
}

