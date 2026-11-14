using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaScriptHeaderTests
{
    private const string Typical =
        "/*\nname: Hollowborn Chaos Envy Run all\ndescription: does all of hollowborn chaos envoy\ntags: hollowborn chaos envoy, hollowborn, do all\n*/\n//cs_include Scripts/CoreBots.cs\n";

    [Fact]
    public void A_typical_header_gives_title_description_and_tags()
    {
        var header = SkuaScriptHeader.Parse(Typical);

        Assert.Equal("Hollowborn Chaos Envy Run all", header.Title);
        Assert.Equal("does all of hollowborn chaos envoy", header.Description);
        Assert.Equal("hollowborn chaos envoy, hollowborn, do all", header.Tags);
    }

    [Fact]
    public void A_bom_and_leading_whitespace_before_the_block_are_allowed()
    {
        Assert.Equal("A", SkuaScriptHeader.Parse("\uFEFF \r\n\t/*\nname: A\n*/").Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("using System;\n/*\nname: A\n*/")]
    [InlineData("// name: A\n/*\nname: A\n*/")]
    [InlineData("/*\nname: A\ndescription: B")]
    [InlineData("/* no keys here */")]
    public void No_block_at_the_start_or_an_unterminated_block_gives_all_null(string text)
    {
        Assert.Equal((null, null, null), SkuaScriptHeader.Parse(text));
    }

    [Fact]
    public void Null_text_gives_all_null()
    {
        Assert.Equal((null, null, null), SkuaScriptHeader.Parse(null!));
    }

    [Fact]
    public void Only_the_first_block_counts()
    {
        var header = SkuaScriptHeader.Parse("/*\nname: First\n*/\n/*\nname: Second\ndescription: Other\n*/");

        Assert.Equal("First", header.Title);
        Assert.Null(header.Description);
    }

    [Fact]
    public void Keys_are_case_insensitive_the_first_occurrence_wins_and_other_keys_are_ignored()
    {
        var header = SkuaScriptHeader.Parse("/*\nNAME:  One \nname: Two\nauthor: me\nDescription:x\nTags : a, b\n*/");

        Assert.Equal("One", header.Title);
        Assert.Equal("x", header.Description);
        Assert.Equal("a, b", header.Tags);
    }

    [Fact]
    public void A_value_never_spans_lines()
    {
        var header = SkuaScriptHeader.Parse("/*\nname: Start\ncontinues here\ndescription: D\n*/");

        Assert.Equal("Start", header.Title);
        Assert.Equal("D", header.Description);
    }

    [Fact]
    public void Crlf_line_endings_leave_no_carriage_return_in_the_values()
    {
        var header = SkuaScriptHeader.Parse("/*\r\nname: A\r\ndescription: B\r\ntags: C\r\n*/\r\n");

        Assert.Equal(("A", "B", "C"), header);
    }

    [Fact]
    public void Empty_values_become_null()
    {
        Assert.Equal((null, "d", null), SkuaScriptHeader.Parse("/*\nname:   \ndescription: d\ntags:\n*/"));
    }

    [Fact]
    public void A_value_with_a_colon_keeps_everything_after_the_first_colon()
    {
        Assert.Equal("Farm: gold", SkuaScriptHeader.Parse("/*\nname: Farm: gold\n*/").Title);
    }

    [Fact]
    public void A_block_end_inside_a_value_ends_the_block_there()
    {
        var header = SkuaScriptHeader.Parse("/*\nname: A */ B\ndescription: late\n*/");

        Assert.Equal("A", header.Title);
        Assert.Null(header.Description);
    }

    [Fact]
    public void Control_characters_become_spaces_and_lone_surrogates_are_removed()
    {
        var header = SkuaScriptHeader.Parse("/*\nname: A\0B\tC\u0007D\ndescription: x\uD800y\n*/");

        Assert.Equal("A B C D", header.Title);
        Assert.Equal("xy", header.Description);
    }

    [Fact]
    public void Values_are_cut_to_their_caps_without_splitting_a_surrogate_pair()
    {
        var pair = "😀";
        var text = "/*\nname: " + new string('a', 99) + pair + "\ndescription: " + new string('b', 300) + "\ntags: " + new string('c', 300) + "\n*/";

        var header = SkuaScriptHeader.Parse(text);

        Assert.Equal(new string('a', 99), header.Title);
        Assert.Equal(200, header.Description!.Length);
        Assert.Equal(120, header.Tags!.Length);
    }

    [Fact]
    public void A_pair_that_ends_exactly_at_the_cap_is_kept()
    {
        var header = SkuaScriptHeader.Parse("/*\nname: " + new string('a', 98) + "😀\n*/");

        Assert.Equal(100, header.Title!.Length);
        Assert.EndsWith("😀", header.Title);
    }

    [Fact]
    public void A_huge_line_does_not_throw_and_only_the_first_4_kb_are_scanned()
    {
        // A long value inside the 4 KB is cut to its cap.
        var header = SkuaScriptHeader.Parse("/*\nname: " + new string('x', 3000) + "\ndescription: kept\n*/");
        Assert.Equal(100, header.Title!.Length);
        Assert.Equal("kept", header.Description);

        // A megabyte line pushes the block end past the scan: it counts as unterminated.
        Assert.Equal((null, null, null), SkuaScriptHeader.Parse("/*\nname: " + new string('x', 1_000_000) + "\n*/"));

        // The end of the block lies past 4 KB: it counts as unterminated.
        Assert.Equal((null, null, null), SkuaScriptHeader.Parse("/*\nname: A\n" + new string(' ', 5000) + "\n*/"));
    }

    [Fact]
    public void A_header_that_ends_inside_the_4_kb_is_read_even_with_a_big_file_after_it()
    {
        Assert.Equal("A", SkuaScriptHeader.Parse("/*\nname: A\n*/\n" + new string('z', 2_000_000)).Title);
    }

    [Fact]
    public void Format_and_bidi_characters_are_stripped_from_the_header_text()
    {
        var header = SkuaScriptHeader.Parse("/*\nname: Ab\u202Ec\u200Bd\u2066e\uFEFFf\ndescription: x\u200Ey\ntags: a\u2067b\n*/");

        Assert.Equal("Abcdef", header.Title);
        Assert.Equal("xy", header.Description);
        Assert.Equal("ab", header.Tags);
        Assert.Null(SkuaScriptHeader.Clean("\u202E\u200B", 20));
    }
}
