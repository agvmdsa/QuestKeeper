using AqwCompanion.App.Infrastructure.Formatting;

namespace AqwCompanion.Tests.Infrastructure;

public class PluralTests
{
    [Theory]
    [InlineData(0, "0 items")]
    [InlineData(1, "1 item")]
    [InlineData(2, "2 items")]
    [InlineData(1500, "1500 items")]
    public void Count_picks_the_singular_only_for_exactly_one(int count, string expected)
    {
        Assert.Equal(expected, Plural.Count(count, "item", "items"));
    }
}
