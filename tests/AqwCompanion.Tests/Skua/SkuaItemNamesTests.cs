using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaItemNamesTests
{
    [Fact]
    public void Names_and_quantities_come_from_the_last_snapshot()
    {
        var names = new SkuaItemNames();
        names.Replace([(1, "Blood Orb", 60), (2, "Dragon Blade", 1)]);

        Assert.Equal("Blood Orb", names.NameOf(1));
        Assert.Equal(60, names.QuantityOf(1));
        Assert.Null(names.NameOf(99));
        Assert.Equal(0, names.QuantityOf(99));
    }

    [Fact]
    public void Replace_forgets_what_the_new_snapshot_does_not_have()
    {
        var names = new SkuaItemNames();
        names.Replace([(1, "Blood Orb", 60)]);

        names.Replace([(2, "Dragon Blade", 1)]);

        Assert.Null(names.NameOf(1));
    }

    [Fact]
    public void A_name_that_is_empty_or_missing_is_not_kept()
    {
        var names = new SkuaItemNames();

        names.Replace([(1, "", 5), (2, null!, 5)]);

        Assert.Null(names.NameOf(1));
        Assert.Null(names.NameOf(2));
    }
}
