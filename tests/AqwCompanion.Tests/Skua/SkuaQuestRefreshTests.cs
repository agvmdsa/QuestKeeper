using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaQuestRefreshTests
{
    [Fact]
    public void Ready_flags_are_read_the_first_time()
    {
        Assert.True(SkuaQuestRefresh.ReadyDue(null, [], TimeSpan.Zero));
        Assert.True(SkuaQuestRefresh.ReadyDue(null, [5], TimeSpan.Zero));
    }

    [Fact]
    public void Ready_flags_are_reused_while_the_same_quests_are_active_and_15_seconds_have_not_passed()
    {
        Assert.False(SkuaQuestRefresh.ReadyDue([5, 7], [5, 7], TimeSpan.FromSeconds(3)));
        Assert.False(SkuaQuestRefresh.ReadyDue([5, 7], [7, 5], TimeSpan.FromSeconds(14.9)));
        Assert.False(SkuaQuestRefresh.ReadyDue([], [], TimeSpan.FromSeconds(14.9)));
    }

    [Fact]
    public void Ready_flags_are_read_again_after_15_seconds()
    {
        Assert.True(SkuaQuestRefresh.ReadyDue([5], [5], TimeSpan.FromSeconds(15)));
        Assert.True(SkuaQuestRefresh.ReadyDue([5], [5], TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Ready_flags_are_read_again_at_once_when_the_active_set_changed()
    {
        Assert.True(SkuaQuestRefresh.ReadyDue([5], [5, 7], TimeSpan.FromSeconds(1)));
        Assert.True(SkuaQuestRefresh.ReadyDue([5, 7], [5], TimeSpan.FromSeconds(1)));
        Assert.True(SkuaQuestRefresh.ReadyDue([5], [6], TimeSpan.Zero));
        Assert.True(SkuaQuestRefresh.ReadyDue([], [6], TimeSpan.Zero));
    }

    [Fact]
    public void The_active_list_keeps_skuas_order_names_blank_ones_and_drops_bad_or_repeated_ids()
    {
        var active = SkuaQuestRefresh.Active([(7, "  Gather Bones "), (5, null), (-1, "Bad"), (7, "Again"), (9, " ")]);

        Assert.Equal([(7, "Gather Bones"), (5, "#5"), (9, "#9")], active);
    }

    [Fact]
    public void The_active_list_is_capped_at_thirty()
    {
        var active = SkuaQuestRefresh.Active(Enumerable.Range(1, 45).Select(i => (i, (string?)$"Q{i}")));

        Assert.Equal(SkuaProtocol.MaxQuests, active.Count);
        Assert.Equal(30, active[^1].Id);
    }

    [Fact]
    public void The_reported_list_uses_the_last_read_flags_and_not_ready_when_unknown()
    {
        var quests = SkuaQuestRefresh.Build([(5, "Slay the Beast"), (7, "Gather Bones")], new Dictionary<int, bool> { [5] = true });

        Assert.Equal([new SkuaQuest(5, "Slay the Beast", true), new SkuaQuest(7, "Gather Bones", false)], quests);
        Assert.Empty(SkuaQuestRefresh.Build([], new Dictionary<int, bool>()));
    }
}
