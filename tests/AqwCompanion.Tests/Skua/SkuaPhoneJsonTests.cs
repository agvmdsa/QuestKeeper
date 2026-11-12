using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaPhoneJsonTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static FeedSnapshot Snapshot() =>
        new(1, new FeedCapture("Listening.", true), null, [], [], null);

    [Fact]
    public void A_full_skua_view_serializes_with_camel_case_names_and_string_enums()
    {
        var view = new SkuaView(
            SkuaConnection.Connected,
            new SkuaHello("0.1.0", "1.4.5.0"),
            new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", true, T0),
            [new SkuaLogEntry(T0, SkuaLogKind.Sold, "Sold Dragon Scale")]);

        var json = PhoneJson.Serialize(new PhoneState(Snapshot(), [], view));

        Assert.Contains("\"connection\":\"Connected\"", json);
        Assert.Contains("\"scriptRunning\":true", json);
        Assert.Contains("\"kind\":\"Sold\"", json);
        Assert.Contains("\"pluginVersion\":\"0.1.0\"", json);
        Assert.Contains("\"skuaVersion\":\"1.4.5.0\"", json);
        Assert.Contains("\"runningSince\":\"2026-11-11T12:00:00+00:00\"", json);
    }

    [Fact]
    public void Active_quests_serialize_with_camel_case_names()
    {
        var view = new SkuaView(
            SkuaConnection.Connected,
            null,
            new SkuaStatus(true, "Hero", null, null, null, false, null, [new SkuaQuest(5, "Slay the Beast", true)]),
            []);

        var json = PhoneJson.Serialize(new PhoneState(Snapshot(), [], view));

        Assert.Contains("\"runningSince\":null,\"quests\":[{\"id\":5,\"name\":\"Slay the Beast\",\"ready\":true}]}", json);
    }

    [Fact]
    public void Quests_not_reported_serialize_as_null_and_none_active_as_an_empty_list()
    {
        var status = new SkuaStatus(true, "Hero", null, null, null, false, null);

        var absent = PhoneJson.Serialize(new PhoneState(Snapshot(), [], new SkuaView(SkuaConnection.Connected, null, status, [])));
        var none = PhoneJson.Serialize(new PhoneState(Snapshot(), [], new SkuaView(SkuaConnection.Connected, null, status with { Quests = [] }, [])));

        Assert.Contains("\"quests\":null", absent);
        Assert.Contains("\"quests\":[]", none);
    }

    [Fact]
    public void No_skua_part_serializes_as_null()
    {
        var json = PhoneJson.Serialize(new PhoneState(Snapshot(), [], null));

        Assert.Contains("\"skua\":null", json);
    }
}
