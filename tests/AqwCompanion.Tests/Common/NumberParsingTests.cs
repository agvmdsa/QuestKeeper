using System.Globalization;
using AqwCompanion.Core.Common;

namespace AqwCompanion.Tests.Common;

public class NumberParsingTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData(" 42 ", 42)]
    [InlineData("1,234,567", 1234567)]
    public void Parses_non_negative_numbers_with_thousands_separators(string text, int expected)
    {
        Assert.True(NumberParsing.TryParseNonNegativeInt(text, NumberStyles.AllowThousands, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void Rejects_anything_else(string? text)
    {
        Assert.False(NumberParsing.TryParseNonNegativeInt(text, NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign, out _));
    }

    [Fact]
    public void Ignores_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.True(NumberParsing.TryParseNonNegativeInt("1,234", NumberStyles.AllowThousands, out var value));
            Assert.Equal(1234, value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(-1, false)]
    public void IsNonNegative_accepts_zero_and_up(int value, bool expected)
    {
        Assert.Equal(expected, NumberParsing.IsNonNegative(value));
    }
}
