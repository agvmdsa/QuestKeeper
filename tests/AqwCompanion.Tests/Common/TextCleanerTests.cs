using AqwCompanion.Core.Common;

namespace AqwCompanion.Tests.Common;

public class TextCleanerTests
{
    [Fact]
    public void Clean_removes_control_characters_and_trims()
    {
        Assert.Equal("HelloWorld", TextCleaner.Clean("  Hel\u0000lo\tWorld\n ", 50));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001\u0002")]
    public void Clean_gives_an_empty_string_for_nothing_visible(string? value)
    {
        Assert.Equal(string.Empty, TextCleaner.Clean(value, 10));
    }

    [Fact]
    public void Clean_caps_to_the_maximum_length()
    {
        Assert.Equal("abc", TextCleaner.Clean("abcdef", 3));
        Assert.Equal("abc", TextCleaner.Clean("abc", 3));
    }

    [Fact]
    public void Clean_never_leaves_a_lone_high_surrogate_at_the_cap()
    {
        var text = "ab" + "\U0001F600" + "cd";

        var cleaned = TextCleaner.Clean(text, 3);

        Assert.Equal("ab", cleaned);
        Assert.Equal("ab" + "\U0001F600", TextCleaner.Clean(text, 4));
    }

    [Fact]
    public void Clean_with_no_room_gives_an_empty_string()
    {
        Assert.Equal(string.Empty, TextCleaner.Clean("abc", 0));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  \t", null)]
    [InlineData("  text ", "text")]
    public void NonBlank_trims_or_gives_null(string? value, string? expected)
    {
        Assert.Equal(expected, TextCleaner.NonBlank(value));
    }
}
