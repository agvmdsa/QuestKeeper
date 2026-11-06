using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.Wiki;

public class WikiTypesTests
{
    [Theory]
    [InlineData("classes", WikiType.Class)]
    [InlineData("armors", WikiType.Armor)]
    [InlineData("weapons", WikiType.Weapon)]
    [InlineData("helmets-hoods", WikiType.Helm)]
    [InlineData("capes-back-items", WikiType.Cape)]
    [InlineData("pets", WikiType.Pet)]
    [InlineData("battle-pets", WikiType.Pet)]
    [InlineData("houses", WikiType.House)]
    [InlineData("floor-items", WikiType.Other)]
    [InlineData("wall-items", WikiType.Other)]
    [InlineData("misc-items", WikiType.Other)]
    [InlineData("use-items", WikiType.Other)]
    [InlineData("necklaces", WikiType.Other)]
    [InlineData("grounds", WikiType.Other)]
    [InlineData("enhancements", WikiType.Other)]
    public void Each_index_maps_to_its_type(string slug, WikiType expected) =>
        Assert.Equal(expected, WikiTypes.ForIndex(slug));

    [Fact]
    public void Every_fetched_index_has_a_type() =>
        Assert.All(WikiUrls.TypeIndexSlugs, slug => Assert.True(Enum.IsDefined(WikiTypes.ForIndex(slug))));

    [Fact]
    public void The_closed_set_is_in_priority_order() =>
        Assert.Equal(
            [WikiType.Class, WikiType.Armor, WikiType.Weapon, WikiType.Helm, WikiType.Cape, WikiType.Pet, WikiType.House, WikiType.Other],
            Enum.GetValues<WikiType>());

    [Theory]
    [InlineData(WikiType.Other, WikiType.Class, WikiType.Class)]
    [InlineData(WikiType.Class, WikiType.Armor, WikiType.Class)]
    [InlineData(WikiType.Weapon, WikiType.Armor, WikiType.Armor)]
    [InlineData(WikiType.House, WikiType.Pet, WikiType.Pet)]
    [InlineData(WikiType.Other, WikiType.Other, WikiType.Other)]
    public void The_higher_priority_type_wins(WikiType a, WikiType b, WikiType expected) =>
        Assert.Equal(expected, WikiTypes.Prefer(a, b));
}
