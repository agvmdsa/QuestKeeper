using System.Globalization;
using AqwCompanion.App.Infrastructure.Formatting;

namespace AqwCompanion.Tests.Infrastructure;

public class LocalTimeTextTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Format_is_the_local_short_date_and_time()
    {
        Assert.Equal(At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), LocalTimeText.Format(At));
    }

    [Fact]
    public void Updated_text_has_the_prefix_or_is_empty_without_a_time()
    {
        Assert.Equal("Last updated " + LocalTimeText.Format(At), UpdatedTextFormatter.Format(At));
        Assert.Equal(string.Empty, UpdatedTextFormatter.Format(null));
    }
}
