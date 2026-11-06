using AqwCompanion.Core.Common;

namespace AqwCompanion.Tests.Common;

public class SafeIntTests
{
    [Theory]
    [InlineData(0L, 0)]
    [InlineData(5L, 5)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MaxValue + 1L, int.MaxValue)]
    [InlineData(long.MaxValue, int.MaxValue)]
    public void Clamp_saturates_at_the_largest_int(long value, int expected)
    {
        Assert.Equal(expected, SafeInt.Clamp(value));
    }

    [Fact]
    public void Clamp_leaves_negative_values_as_they_are()
    {
        Assert.Equal(-3, SafeInt.Clamp(-3L));
    }
}
