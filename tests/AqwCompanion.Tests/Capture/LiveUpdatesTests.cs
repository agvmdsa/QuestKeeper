using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Reputation;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;
using static AqwCompanion.Tests.Capture.SecretScan;

namespace AqwCompanion.Tests.Capture;

public class LiveUpdatesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static CharacterProfile Profile(long gold = 1000, long exp = 500, int coins = 20, int classPoints = 300) =>
        new("Fictional Hero", 60, exp, 9000, gold, coins, classPoints, 300, 90, 80, 70, 5, true, 25, 10, 20, 30, 40);

    private static OwnedItem Owned(string name, int charItemId, int quantity = 1, int itemId = 1, ItemFlags flags = ItemFlags.None) =>
        new(name, "Item", quantity, 100, 5, flags, itemId, null, charItemId);

    private static CharacterSnapshot Snapshot(CharacterProfile? profile = null, params OwnedItem[] items) =>
        new(
            profile ?? Profile(),
            [new FactionStanding(5, "Fictional Order", 3, 5000, 6400, 1400)],
            items.Length == 0 ? [Owned("Potion", 701, 3, 11), Owned("Scroll", 702, 1, 12)] : items,
            [Owned("Chair", 900, 1, 13, ItemFlags.House)],
            At);

    private static LiveUpdateResult Apply(CharacterSnapshot snapshot, Dictionary<string, object?> envelope, LiveEventCache? cache = null)
    {
        using var message = MessageDecoder.Decode(PlainJson(envelope));
        Assert.NotNull(message);
        return LiveUpdates.Apply(snapshot, message, cache ?? new LiveEventCache());
    }

    private static Dictionary<string, object?> Body(params (string Key, object? Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    private static Dictionary<string, object?> GoldExp(object? gold, object? exp, string type = "m") =>
        Envelope("addGoldExp", Body(("intGold", gold), ("intExp", exp), ("typ", type), ("id", 3)));

    private static Dictionary<string, object?> Entry(int charItemId, int qty, int qtyNow, bool bank = false) =>
        new()
        {
            ["CharItemID"] = charItemId.ToString(),
            ["bBank"] = bank ? 1 : 0,
            ["iQty"] = qty,
            ["iQtyNow"] = qtyNow,
        };

    private static Dictionary<string, object?> AddItems(params Dictionary<string, object?>[] entries) =>
        Envelope("addItems", Body(("items", entries.ToDictionary(e => (string)e["CharItemID"]!, e => (object?)e))));

    private static Dictionary<string, object?> DropItem(int itemId, string name, string type = "Item", int level = 30, params string[] flags)
    {
        var definition = new Dictionary<string, object?>
        {
            ["ItemID"] = itemId.ToString(),
            ["sName"] = name,
            ["sType"] = type,
            ["iLvl"] = level,
            ["iQty"] = 1,
            ["iStk"] = 10,
            ["sDesc"] = "A fictional drop. " + SecretPlayer,
            ["sMeta"] = SecretToken,
        };
        foreach (var flag in flags)
        {
            definition[flag] = 1;
        }

        return Envelope("dropItem", Body(("items", new Dictionary<string, object?> { [itemId.ToString()] = definition })));
    }

    private static Dictionary<string, object?> GetDrop(int itemId, int charItemId, int qtyNow, object? success = null, bool bank = false) =>
        Envelope("getDrop", Body(
            ("bSuccess", success ?? 1), ("ItemID", itemId), ("CharItemID", charItemId), ("iQty", 1), ("iQtyNow", qtyNow), ("bBank", bank ? 1 : 0)));

    private static Dictionary<string, object?> SellItem(int charItemId, int qty, int qtyNow, int amount, bool coins = false) =>
        Envelope("sellItem", Body(("CharItemID", charItemId), ("iQty", qty), ("iQtyNow", qtyNow), ("intAmount", amount), ("bCoins", coins ? 1 : 0)));

    // The tests below that mention gold and exp rest on ONE assumption: the events carry the amounts GAINED, not new totals.

    [Fact]
    public void Assumption_gold_and_exp_events_carry_gains_not_totals()
    {
        Assert.True(LiveUpdates.GoldExpEventsCarryGains);

        var result = Apply(Snapshot(), GoldExp(40, 15));

        Assert.Equal((1040L, 515L), (result.Snapshot.Profile.Gold, result.Snapshot.Profile.Exp));
        Assert.Equal("applied", result.Outcome);
        Assert.True(result.Changed);
    }

    [Fact]
    public void Gold_and_exp_gains_may_arrive_as_text_and_add_up_over_several_events()
    {
        var first = Apply(Snapshot(), GoldExp("40", "15"));
        var second = Apply(first.Snapshot, GoldExp(60L, 5));

        Assert.Equal((1100L, 520L), (second.Snapshot.Profile.Gold, second.Snapshot.Profile.Exp));
    }

    [Fact]
    public void Long_values_do_not_overflow_and_negative_totals_clamp_to_zero()
    {
        var big = Snapshot(Profile(gold: long.MaxValue - 5, exp: long.MaxValue - 5));
        var grown = Apply(big, GoldExp(long.MaxValue, long.MaxValue));
        Assert.Equal((long.MaxValue, long.MaxValue), (grown.Snapshot.Profile.Gold, grown.Snapshot.Profile.Exp));

        var shrunk = Apply(Snapshot(Profile(gold: 10, exp: 10)), GoldExp(-500, -500));
        Assert.Equal((0L, 0L), (shrunk.Snapshot.Profile.Gold, shrunk.Snapshot.Profile.Exp));
    }

    [Fact]
    public void A_gold_event_without_amounts_is_ignored()
    {
        var snapshot = Snapshot();

        var result = Apply(snapshot, Envelope("addGoldExp", Body(("typ", "m"))));

        Assert.Equal("ignored", result.Outcome);
        Assert.False(result.Changed);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void Add_items_sets_the_quantity_of_the_item_with_that_char_item_id()
    {
        var result = Apply(Snapshot(), AddItems(Entry(702, 1, 9)));

        Assert.Equal([3, 9], result.Snapshot.Items.Select(i => i.Quantity));
        Assert.Equal("applied", result.Outcome);
    }

    [Fact]
    public void Add_items_with_an_unknown_char_item_id_is_unmatched_and_changes_nothing()
    {
        var snapshot = Snapshot();

        var result = Apply(snapshot, AddItems(Entry(555, 1, 9)));

        Assert.Equal("unmatched", result.Outcome);
        Assert.False(result.Changed);
        Assert.Equal(snapshot.Items, result.Snapshot.Items);
    }

    [Fact]
    public void Drop_item_only_caches_the_definition_and_changes_nothing()
    {
        var snapshot = Snapshot();
        var cache = new LiveEventCache();

        var result = Apply(snapshot, DropItem(77, "Fictional Axe", "Axe", 42), cache);

        Assert.Equal("cached", result.Outcome);
        Assert.False(result.Changed);
        Assert.Same(snapshot, result.Snapshot);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void A_get_drop_after_a_drop_item_creates_the_item_from_the_cached_definition()
    {
        var cache = new LiveEventCache();
        Apply(Snapshot(), DropItem(77, "Fictional Axe", "Axe", 42, "bCoins", "bUpg", "bQuest", "bTemp"), cache);

        var result = Apply(Snapshot(), GetDrop(77, 8800, 2), cache);

        Assert.Equal("applied", result.Outcome);
        var created = result.Snapshot.Items[^1];
        Assert.Equal(("Fictional Axe", "Axe", 2, 42, 77, 8800), (created.Name, created.Type, created.Quantity, created.Level, created.ItemId, created.CharItemId));
        Assert.Equal(ItemFlags.Coins | ItemFlags.Upgrade | ItemFlags.Quest | ItemFlags.Temp, created.Flags);
        Assert.Equal(3, result.Snapshot.Items.Count);
    }

    [Fact]
    public void A_get_drop_to_the_bank_marks_the_new_item_as_banked()
    {
        var cache = new LiveEventCache();
        Apply(Snapshot(), DropItem(77, "Fictional Axe"), cache);

        var result = Apply(Snapshot(), GetDrop(77, 8800, 1, bank: true), cache);

        Assert.True(result.Snapshot.Items[^1].Flags.HasFlag(ItemFlags.Bank));
    }

    [Fact]
    public void A_get_drop_of_an_owned_item_sets_its_quantity()
    {
        var result = Apply(Snapshot(), GetDrop(11, 701, 4));

        Assert.Equal([4, 1], result.Snapshot.Items.Select(i => i.Quantity));
        Assert.Equal("applied", result.Outcome);
    }

    [Fact]
    public void A_get_drop_of_an_unknown_item_without_a_cached_definition_is_unmatched()
    {
        var snapshot = Snapshot();

        var result = Apply(snapshot, GetDrop(77, 8800, 1));

        Assert.Equal("unmatched", result.Outcome);
        Assert.False(result.Changed);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(false)]
    [InlineData("0")]
    public void A_get_drop_that_did_not_succeed_changes_nothing(object success)
    {
        var cache = new LiveEventCache();
        Apply(Snapshot(), DropItem(77, "Fictional Axe"), cache);
        var snapshot = Snapshot();

        var result = Apply(snapshot, GetDrop(77, 8800, 1, success), cache);

        Assert.False(result.Changed);
        Assert.Equal("ignored", result.Outcome);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void The_drop_cache_keeps_the_newest_sixty_four_definitions_and_the_newest_wins_per_item()
    {
        var cache = new LiveEventCache();
        for (var id = 1; id <= 70; id++)
        {
            Apply(Snapshot(), DropItem(id, "Drop " + id), cache);
        }

        Apply(Snapshot(), DropItem(70, "Drop 70 again"), cache);

        Assert.Equal(LiveEventCache.MaxDefinitions, cache.Count);
        Assert.False(cache.TryGet(1, out _));
        Assert.False(cache.TryGet(6, out _));
        Assert.True(cache.TryGet(7, out _));
        Assert.True(cache.TryGet(70, out var newest));
        Assert.Equal("Drop 70 again", newest.Name);
    }

    [Fact]
    public void Selling_sets_the_quantity_and_adds_the_gold()
    {
        var result = Apply(Snapshot(), SellItem(701, 1, 2, 150));

        Assert.Equal([2, 1], result.Snapshot.Items.Select(i => i.Quantity));
        Assert.Equal((1150L, 20), (result.Snapshot.Profile.Gold, result.Snapshot.Profile.Coins));
    }

    [Fact]
    public void Selling_for_coins_adds_coins_and_not_gold()
    {
        var result = Apply(Snapshot(), SellItem(701, 1, 2, 150, coins: true));

        Assert.Equal((1000L, 170), (result.Snapshot.Profile.Gold, result.Snapshot.Profile.Coins));
    }

    [Fact]
    public void Selling_the_last_one_removes_the_item()
    {
        var result = Apply(Snapshot(), SellItem(702, 1, 0, 30));

        Assert.Equal(["Potion"], result.Snapshot.Items.Select(i => i.Name));
        Assert.Equal(1030L, result.Snapshot.Profile.Gold);
    }

    [Fact]
    public void Selling_an_unknown_item_for_nothing_is_unmatched()
    {
        var snapshot = Snapshot();

        var result = Apply(snapshot, SellItem(555, 1, 0, 0));

        Assert.Equal("unmatched", result.Outcome);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void Update_class_sets_the_class_points()
    {
        var result = Apply(Snapshot(), Envelope("updateClass", Body(("iCP", "12345"), ("sClassName", "Fictional Class"), ("uid", 1))));

        Assert.Equal(12345, result.Snapshot.Profile.ClassPoints);
        Assert.Equal("applied", result.Outcome);
    }

    [Fact]
    public void Update_class_never_sets_negative_class_points_and_needs_the_field()
    {
        var negative = Apply(Snapshot(), Envelope("updateClass", Body(("iCP", -5))));
        var missing = Apply(Snapshot(), Envelope("updateClass", Body(("sClassName", "X"))));

        Assert.Equal(0, negative.Snapshot.Profile.ClassPoints);
        Assert.Equal("ignored", missing.Outcome);
    }

    [Fact]
    public void An_unknown_command_is_ignored_and_returns_the_same_snapshot()
    {
        var snapshot = Snapshot();

        var result = Apply(snapshot, Envelope("mtls", Body(("id", 1))));

        Assert.Equal("ignored", result.Outcome);
        Assert.False(result.Changed);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void The_live_commands_are_exactly_the_six_events()
    {
        Assert.Equal(
            ["addGoldExp", "addItems", "dropItem", "getDrop", "sellItem", "updateClass"],
            LiveUpdates.Commands.Order());
        Assert.All(LiveUpdates.Commands, name => Assert.True(LiveUpdates.IsLiveCommand(name)));
        Assert.False(LiveUpdates.IsLiveCommand("initUserData"));
        Assert.False(LiveUpdates.IsLiveCommand(null));
    }

    [Fact]
    public void The_snapshot_is_never_mutated_a_new_instance_comes_back()
    {
        var snapshot = Snapshot();
        var itemsBefore = snapshot.Items.ToList();
        var profileBefore = snapshot.Profile;

        var result = Apply(snapshot, SellItem(702, 1, 0, 30));
        Apply(snapshot, GoldExp(5, 5));
        Apply(snapshot, AddItems(Entry(701, 1, 99)));

        Assert.NotSame(snapshot, result.Snapshot);
        Assert.NotSame(snapshot.Items, result.Snapshot.Items);
        Assert.Equal(itemsBefore, snapshot.Items);
        Assert.Equal(profileBefore, snapshot.Profile);
        Assert.Equal(At, result.Snapshot.CapturedAtUtc);
        Assert.Equal(snapshot.Factions, result.Snapshot.Factions);
        Assert.Equal(snapshot.HouseItems, result.Snapshot.HouseItems);
    }

    [Fact]
    public void Extra_fields_carrying_marker_secrets_never_reach_the_result()
    {
        var cache = new LiveEventCache();
        var noisy = new Dictionary<string, object?>
        {
            ["strEmail"] = SecretEmail, ["strIP"] = SecretIp, ["sMeta"] = SecretToken, ["uName"] = SecretPlayer, ["pass"] = SecretPassword,
        };
        var events = new[]
        {
            GoldExp(5, 5), AddItems(Entry(701, 1, 9)), DropItem(77, "Fictional Axe"), GetDrop(77, 8800, 1),
            SellItem(702, 1, 0, 30), Envelope("updateClass", Body(("iCP", 9))),
        };

        var current = Snapshot();
        foreach (var envelope in events)
        {
            foreach (var (key, value) in noisy)
            {
                ((Dictionary<string, object?>)((Dictionary<string, object?>)envelope["b"]!)["o"]!)[key] = value;
            }

            var result = Apply(current, envelope, cache);
            AssertNoSecrets(result);
            current = result.Snapshot;
        }

        AssertNoSecrets(cache);
        AssertNoSecrets(current);
    }

    [Fact]
    public void Garbage_shapes_do_not_throw()
    {
        var snapshot = Snapshot();
        Dictionary<string, object?>[] events =
        [
            Envelope("addItems", Body(("items", "not an object"))),
            Envelope("addItems", Body(("items", new List<object?> { 1, 2 }))),
            Envelope("dropItem", Body(("items", new Dictionary<string, object?> { ["x"] = 5 }))),
            Envelope("getDrop", Body(("bSuccess", "yes"))),
            Envelope("sellItem", Body(("CharItemID", "abc"))),
            Envelope("updateClass", Body(("iCP", new List<object?>()))),
            Envelope("addGoldExp", Body(("intGold", new Dictionary<string, object?>()))),
        ];

        foreach (var envelope in events)
        {
            var thrown = Record.Exception(() => Apply(snapshot, envelope));
            Assert.Null(thrown);
        }
    }
}
