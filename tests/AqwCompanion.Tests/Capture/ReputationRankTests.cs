using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Capture;

public class ReputationRankTests
{
    [Theory]
    [InlineData(0, 1, 0, 900)]
    [InlineData(899, 1, 899, 900)]
    [InlineData(900, 2, 0, 2700)]
    [InlineData(3599, 2, 2699, 2700)]
    [InlineData(3600, 3, 0, 6400)]
    [InlineData(5000, 3, 1400, 6400)]
    [InlineData(22500, 5, 0, 21600)]
    [InlineData(80000, 7, 1600, 51200)]
    [InlineData(192600, 8, 63000, 72900)]
    [InlineData(302500, 10, 0, 0)]
    [InlineData(999999999, 10, 0, 0)]
    [InlineData(-5, 1, 0, 900)]
    public void Standing_follows_the_wiki_table(long total, int rank, int spill, int toRank)
    {
        var standing = ReputationRank.Standing(7, "Fictional Faction", total);

        Assert.Equal(7, standing.FactionId);
        Assert.Equal("Fictional Faction", standing.Name);
        Assert.Equal(rank, standing.Rank);
        Assert.Equal(spill, standing.SpillRep);
        Assert.Equal(toRank, standing.RepToRank);
    }

    [Fact]
    public void Rep_is_the_total()
    {
        Assert.Equal(5000, ReputationRank.Standing(1, "F", 5000).Rep);
    }

    [Fact]
    public void Rep_is_clamped_to_the_int_range()
    {
        Assert.Equal(int.MaxValue, ReputationRank.Standing(1, "F", 99_999_999_999).Rep);
    }

    [Fact]
    public void Negative_total_gives_zero_rep()
    {
        Assert.Equal(0, ReputationRank.Standing(1, "F", -1).Rep);
    }
}
