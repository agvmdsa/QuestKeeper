using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaFactionReadTests
{
    private static readonly SkuaFaction Nation = new(1, "Nation", 3, 5000, 300, 1200);

    [Fact]
    public void A_read_becomes_the_list_in_skuas_order()
    {
        var factions = SkuaFactionRead.Next(null, [(20, "Good", 10, 302500, 0, 0), (1, "Nation", 3, 5000, 300, 1200)]);

        Assert.Equal([new SkuaFaction(20, "Good", 10, 302500, 0, 0), Nation], factions);
    }

    [Fact]
    public void A_failed_read_keeps_the_last_list()
    {
        IReadOnlyList<SkuaFaction> last = [Nation];

        Assert.Same(last, SkuaFactionRead.Next(last, null));
        Assert.Null(SkuaFactionRead.Next(null, null));
    }

    [Fact]
    public void An_empty_read_is_an_empty_list()
    {
        Assert.Empty(SkuaFactionRead.Next([Nation], [])!);
    }

    [Fact]
    public void At_most_sixty_are_kept()
    {
        var read = Enumerable.Range(1, 70).Select(i => (i, (string?)$"F{i}", 1, 1, 1, 900));

        var factions = SkuaFactionRead.Next(null, read)!;

        Assert.Equal(SkuaProtocol.MaxFactions, factions.Count);
        Assert.Equal(60, factions[^1].Id);
    }

    [Fact]
    public void Negative_or_repeated_ids_are_left_out_and_names_trimmed_or_shown_as_the_id()
    {
        var factions = SkuaFactionRead.Next(null, [(-1, "Neg", 1, 1, 1, 1), (1, "  Nation ", 3, 5000, 300, 1200), (1, "Again", 1, 1, 1, 1), (7, "  ", 1, 1, 1, 1), (8, null, 1, 1, 1, 1)]);

        Assert.Equal([Nation, new SkuaFaction(7, "#7", 1, 1, 1, 1), new SkuaFaction(8, "#8", 1, 1, 1, 1)], factions);
    }

    [Fact]
    public void Negative_numbers_are_sent_as_zero_so_the_entry_is_not_lost()
    {
        var factions = SkuaFactionRead.Next(null, [(1, "Nation", -1, -5, -2, -3)]);

        Assert.Equal([new SkuaFaction(1, "Nation", 0, 0, 0, 0)], factions);
    }
}
