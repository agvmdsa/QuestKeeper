using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.Wiki;

public class HardFarmModelsTests
{
    [Fact]
    public void Entries_are_flattened_distinct_by_slug_with_the_first_group_winning()
    {
        var list = new HardFarmList(
            [new HardFarmGroup("One", ["a", "b"]), new HardFarmGroup("Two", ["b", "c"])], "credit");

        Assert.Equal([new HardFarmEntry("a", "One"), new HardFarmEntry("b", "One"), new HardFarmEntry("c", "Two")], list.Entries);
        Assert.Equal(3, list.Count);
        Assert.Equal("credit", list.Credit);
    }

    [Fact]
    public void An_empty_list_has_no_entries()
    {
        var list = new HardFarmList([], null);

        Assert.Empty(list.Entries);
        Assert.Equal(0, list.Count);
    }
}
