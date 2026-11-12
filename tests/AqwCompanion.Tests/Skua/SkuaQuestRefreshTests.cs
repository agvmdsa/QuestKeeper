using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaQuestRefreshTests
{
    [Fact]
    public void The_list_is_read_the_first_time()
    {
        Assert.True(SkuaQuestRefresh.ReadDue(everRead: false, forced: false, TimeSpan.Zero));
    }

    [Fact]
    public void The_list_is_not_read_again_before_15_seconds()
    {
        Assert.False(SkuaQuestRefresh.ReadDue(everRead: true, forced: false, TimeSpan.FromSeconds(3)));
        Assert.False(SkuaQuestRefresh.ReadDue(everRead: true, forced: false, TimeSpan.FromSeconds(14.9)));
    }

    [Fact]
    public void The_list_is_read_again_after_15_seconds()
    {
        Assert.True(SkuaQuestRefresh.ReadDue(everRead: true, forced: false, TimeSpan.FromSeconds(15)));
        Assert.True(SkuaQuestRefresh.ReadDue(everRead: true, forced: false, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void A_forced_reason_reads_at_once()
    {
        Assert.True(SkuaQuestRefresh.ReadDue(everRead: true, forced: true, TimeSpan.Zero));
    }

    [Fact]
    public void Ready_comes_from_the_completed_status()
    {
        var quests = SkuaQuestRefresh.Next(null, [(5, "Slay the Beast", "c"), (7, "Gather Bones", "p"), (8, "Odd", "C"), (9, "None", null)]);

        Assert.Equal(
            [new SkuaQuest(5, "Slay the Beast", true), new SkuaQuest(7, "Gather Bones", false), new SkuaQuest(8, "Odd", false), new SkuaQuest(9, "None", false)],
            quests);
    }

    [Fact]
    public void The_list_keeps_skuas_order_names_blank_ones_and_drops_bad_or_repeated_ids()
    {
        var quests = SkuaQuestRefresh.Next(null, [(7, "  Gather Bones ", "c"), (5, null, "p"), (-1, "Bad", "c"), (7, "Again", "p"), (9, " ", "p")]);

        Assert.Equal([new SkuaQuest(7, "Gather Bones", true), new SkuaQuest(5, "#5", false), new SkuaQuest(9, "#9", false)], quests);
    }

    [Fact]
    public void The_list_is_capped_at_thirty()
    {
        var quests = SkuaQuestRefresh.Next(null, Enumerable.Range(1, 45).Select(i => (i, (string?)$"Q{i}", (string?)"p")))!;

        Assert.Equal(SkuaProtocol.MaxQuests, quests.Count);
        Assert.Equal(30, quests[^1].Id);
    }

    [Fact]
    public void An_empty_read_reports_no_active_quests()
    {
        Assert.Empty(SkuaQuestRefresh.Next([new SkuaQuest(1, "Old", false)], [])!);
    }

    [Fact]
    public void A_failed_read_keeps_the_last_list_and_reports_nothing_known_when_there_never_was_one()
    {
        IReadOnlyList<SkuaQuest> last = [new SkuaQuest(5, "Slay the Beast", true)];

        Assert.Same(last, SkuaQuestRefresh.Next(last, null));
        Assert.Null(SkuaQuestRefresh.Next(null, null));
    }
}
