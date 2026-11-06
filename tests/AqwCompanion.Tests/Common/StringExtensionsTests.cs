using AqwCompanion.Core.Common;

namespace AqwCompanion.Tests.Common;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("Class", "class", true)]
    [InlineData("Class", "Classes", false)]
    [InlineData(null, null, true)]
    [InlineData("a", null, false)]
    [InlineData(null, "a", false)]
    public void EqualsIgnoreCase_compares_ordinally_ignoring_case(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, a.EqualsIgnoreCase(b));
    }

    [Fact]
    public void EqualsIgnoreCase_is_not_culture_sensitive()
    {
        Assert.False("i".EqualsIgnoreCase("İ"));
    }
}
