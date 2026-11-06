using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.Wiki;

public class RobotsTxtTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("# only a comment\n")]
    [InlineData("User-agent: *\nDisallow:\n")]
    [InlineData("User-agent: Googlebot\nDisallow: /\n")]
    public void Everything_is_allowed_without_a_matching_rule(string? text)
    {
        var robots = RobotsTxt.Parse(text);

        Assert.True(robots.IsAllowed("/syfarm"));
        Assert.True(robots.IsAllowed("/"));
    }

    [Fact]
    public void Disallow_is_a_prefix_match()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /admin\nDisallow: /search:\n");

        Assert.False(robots.IsAllowed("/admin"));
        Assert.False(robots.IsAllowed("/admin:manage/x"));
        Assert.False(robots.IsAllowed("/search:site/q/a"));
        Assert.True(robots.IsAllowed("/syfarm"));
        Assert.True(robots.IsAllowed("/adm"));
    }

    [Fact]
    public void Disallow_slash_blocks_everything()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /\n");

        Assert.False(robots.IsAllowed("/syfarm"));
        Assert.False(robots.IsAllowed("/"));
    }

    [Fact]
    public void Wildcards_and_the_end_anchor_work()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*/edit\nDisallow: /*.pdf$\nDisallow: /exact$\n");

        Assert.False(robots.IsAllowed("/page/edit"));
        Assert.False(robots.IsAllowed("/a/b/edit/more"));
        Assert.False(robots.IsAllowed("/file.pdf"));
        Assert.True(robots.IsAllowed("/file.pdf?x=1"));
        Assert.True(robots.IsAllowed("/file.pdfx"));
        Assert.False(robots.IsAllowed("/exact"));
        Assert.True(robots.IsAllowed("/exact/more"));
        Assert.True(robots.IsAllowed("/syfarm"));
    }

    [Fact]
    public void The_longest_rule_wins_and_allow_wins_a_tie()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /private\nAllow: /private/open\nDisallow: /tie\nAllow: /tie\n");

        Assert.False(robots.IsAllowed("/private/closed"));
        Assert.True(robots.IsAllowed("/private/open/page"));
        Assert.True(robots.IsAllowed("/tie"));
    }

    [Fact]
    public void The_exact_agent_group_wins_over_the_star_group()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /\n\nUser-agent: AqwCompanion\nDisallow: /secret\n");

        Assert.True(robots.IsAllowed("/syfarm"));
        Assert.False(robots.IsAllowed("/secret"));
    }

    [Fact]
    public void An_agent_group_with_no_rules_allows_everything_even_when_star_blocks()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /\n\nUser-agent: aqwcompanion\nDisallow:\n");

        Assert.True(robots.IsAllowed("/syfarm"));
    }

    [Fact]
    public void Several_agent_lines_share_one_group_and_comments_and_case_are_tolerated()
    {
        var robots = RobotsTxt.Parse(
            "USER-AGENT: Bingbot\r\nuser-agent: *   # everyone\r\nDISALLOW: /nope # why\r\n\r\nUser-agent: Other\r\nDisallow: /other\r\n");

        Assert.False(robots.IsAllowed("/nope"));
        Assert.True(robots.IsAllowed("/other"));
    }

    [Fact]
    public void Garbage_lines_are_ignored()
    {
        var robots = RobotsTxt.Parse("<html>nope</html>\n::\nDisallow /x\nSitemap: https://x/y\nUser-agent: *\nDisallow: /z\n");

        Assert.False(robots.IsAllowed("/z"));
        Assert.True(robots.IsAllowed("/x"));
    }

    [Fact]
    public void An_empty_path_is_the_root()
    {
        Assert.False(RobotsTxt.Parse("User-agent: *\nDisallow: /\n").IsAllowed(""));
    }
}
