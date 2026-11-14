using System.Text;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaScriptFileTextTests
{
    private const string Bot = "/*\nname: My bot\n*/\npublic void ScriptMain(IScriptInterface Bot) {}";
    private const string Library = "public class Lib { }";

    private static byte[] WithBom(Encoding e, string text) => [.. e.GetPreamble(), .. e.GetBytes(text)];

    private static SkuaScriptFacts Facts(byte[] bytes, bool truncated = false) => SkuaScriptFileText.Facts(bytes, bytes.Length, truncated);

    public static TheoryData<string, byte[]> Encodings => new()
    {
        { "utf8 no bom", Encoding.UTF8.GetBytes(Bot) },
        { "utf8 bom", WithBom(new UTF8Encoding(true), Bot) },
        { "utf16 le bom", WithBom(Encoding.Unicode, Bot) },
        { "utf16 be bom", WithBom(Encoding.BigEndianUnicode, Bot) },
        { "utf32 le bom", WithBom(new UTF32Encoding(false, true), Bot) },
        { "utf32 be bom", WithBom(new UTF32Encoding(true, true), Bot) },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void A_bot_is_recognised_in_every_encoding_with_its_header(string _, byte[] bytes)
    {
        var facts = Facts(bytes);

        Assert.True(facts.IsBot);
        Assert.Equal("My bot", facts.Title);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_library_in_utf16_with_a_bom_is_still_a_library(bool bigEndian)
    {
        var enc = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;

        Assert.False(Facts(WithBom(enc, Library)).IsBot);
    }

    [Fact]
    public void Utf16_without_a_bom_decodes_to_nul_characters_and_is_listed_as_undecidable()
    {
        var facts = Facts(Encoding.Unicode.GetBytes(Library));

        Assert.True(facts.IsBot);
        Assert.Null(facts.Title);
    }

    [Fact]
    public void Plain_utf8_without_ScriptMain_is_a_library()
    {
        Assert.False(Facts(Encoding.UTF8.GetBytes(Library)).IsBot);
        Assert.False(Facts([]).IsBot);
    }

    [Fact]
    public void Text_cut_at_the_read_cap_without_ScriptMain_is_listed_but_a_seen_ScriptMain_still_counts()
    {
        Assert.True(Facts(Encoding.UTF8.GetBytes(Library), truncated: true).IsBot);
        Assert.True(Facts(Encoding.UTF8.GetBytes(Bot), truncated: true).IsBot);
    }

    [Fact]
    public void A_truncated_header_is_still_read_and_only_the_bytes_counted_are_used()
    {
        var bytes = Encoding.UTF8.GetBytes(Bot + "\0\0\0 junk");
        var facts = SkuaScriptFileText.Facts(bytes, Encoding.UTF8.GetByteCount(Bot), false);

        Assert.True(facts.IsBot);
        Assert.Equal("My bot", facts.Title);
    }

    [Fact]
    public void A_cut_in_the_middle_of_a_utf16_character_does_not_throw()
    {
        var bytes = WithBom(Encoding.Unicode, Bot);

        var facts = SkuaScriptFileText.Facts(bytes, bytes.Length - 1, true);

        Assert.True(facts.IsBot);
    }

    [Fact]
    public void Null_bytes_or_a_bad_count_do_not_throw()
    {
        Assert.True(SkuaScriptFileText.Facts(null!, 5, false).IsBot);
        Assert.True(SkuaScriptFileText.Facts([1, 2], 99, false).IsBot);
        Assert.True(SkuaScriptFileText.Facts([1, 2], -1, false).IsBot);
    }
}
