using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.Tests.Phone;

public class NoticeEngineSkuaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static SkuaInstanceView Instance(string id, string label, bool disconnected) =>
        new(id, label, disconnected ? SkuaConnection.NotConnected : SkuaConnection.Connected, null, null, [], disconnected);

    private static SkuaHubView Hub(params SkuaInstanceView[] instances) => new(instances);

    [Fact]
    public void The_first_view_is_a_baseline_even_with_a_disconnected_instance()
    {
        var engine = new NoticeEngine();

        Assert.Empty(engine.ProcessSkua(Hub(Instance("a", "Hero", true)), T0));
    }

    [Fact]
    public void A_connected_instance_that_disconnects_raises_one_notice_with_its_label()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(Hub(Instance("a", "Hero", false)), T0);

        var notice = Assert.Single(engine.ProcessSkua(Hub(Instance("a", "Hero", true)), T0.AddSeconds(1)));
        Assert.Empty(engine.ProcessSkua(Hub(Instance("a", "Hero", true)), T0.AddSeconds(2)));

        Assert.Equal(NoticeKind.SkuaDisconnected, notice.Kind);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Skua disconnected", notice.Title);
        Assert.Equal("Hero is no longer connected to SkuaLink.", notice.Body);
    }

    [Fact]
    public void A_disconnected_instance_that_comes_back_raises_a_reconnected_notice()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(Hub(Instance("a", "Skua 1", true)), T0);

        var notice = Assert.Single(engine.ProcessSkua(Hub(Instance("a", "Hero", false)), T0.AddSeconds(1)));

        Assert.Equal(NoticeKind.SkuaReconnected, notice.Kind);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Skua reconnected", notice.Title);
        Assert.Equal("Hero is connected to SkuaLink again.", notice.Body);
    }

    [Fact]
    public void A_new_instance_or_one_that_goes_away_raises_nothing()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(Hub(Instance("a", "Hero", false)), T0);

        Assert.Empty(engine.ProcessSkua(Hub(Instance("a", "Hero", false), Instance("b", "Zed", false)), T0.AddSeconds(1)));
        Assert.Empty(engine.ProcessSkua(Hub(Instance("b", "Zed", false)), T0.AddSeconds(2)));
    }

    [Fact]
    public void Each_disconnect_is_its_own_notice()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(Hub(Instance("a", "Hero", false)), T0);

        Assert.Single(engine.ProcessSkua(Hub(Instance("a", "Hero", true)), T0.AddSeconds(1)));
        Assert.Single(engine.ProcessSkua(Hub(Instance("a", "Hero", false)), T0.AddSeconds(2)));
        Assert.Single(engine.ProcessSkua(Hub(Instance("a", "Hero", true)), T0.AddSeconds(3)));
    }

    [Fact]
    public void Reset_makes_the_next_view_a_baseline_again()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(Hub(Instance("a", "Hero", false)), T0);

        engine.Reset();

        Assert.Empty(engine.ProcessSkua(Hub(Instance("a", "Hero", true)), T0.AddSeconds(1)));
    }
}
