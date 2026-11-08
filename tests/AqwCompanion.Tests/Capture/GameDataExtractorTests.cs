using System.Text.Json;
using AqwCompanion.Core.Capture;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;
using static AqwCompanion.Tests.Capture.SecretScan;

namespace AqwCompanion.Tests.Capture;

public class GameDataExtractorTests
{
    private static GameDataPart? Extract(object json)
    {
        using var message = MessageDecoder.Decode(PlainJson(json));
        Assert.NotNull(message);
        return GameDataExtractor.Extract(message);
    }

    private static InventoryPart Inventory(object json) => Assert.IsType<InventoryPart>(Extract(json));

    private static CharacterProfile Profile(object json) => Assert.IsType<ProfilePart>(Extract(json)).Profile;

    [Fact]
    public void A_full_inventory_is_extracted_with_counts_flags_ids_and_house_flag()
    {
        var part = Inventory(LoadInventoryBig(
            [Faction(5, "Fictional Order", 5000), Faction(6, "Other Order", 302500)],
            [
                Item("Fictional Blade", "Sword", itemId: 11, qty: 2, stack: 5, level: 30, coins: true, upgrade: true, equip: true),
                Item("Fictional Class", "Class", itemId: 12, wear: true, quest: true, bank: true, temp: true, classId: 99),
                Item("Plain Thing"),
            ],
            [Item("Fictional Chair", "House Item", itemId: 13)]));

        Assert.Equal(2, part.Factions.Count);
        Assert.Equal((5, "Fictional Order", 3, 5000, 6400, 1400), Tuple(part.Factions[0]));
        Assert.Equal((6, "Other Order", 10, 302500, 0, 0), Tuple(part.Factions[1]));

        Assert.Equal(3, part.Items.Count);
        var blade = part.Items[0];
        Assert.Equal(new OwnedItem("Fictional Blade", "Sword", 2, 5, 30, ItemFlags.Coins | ItemFlags.Upgrade | ItemFlags.Equipped, 11, null), blade);
        Assert.Equal(
            new OwnedItem("Fictional Class", "Class", 1, 1, 1, ItemFlags.Worn | ItemFlags.Quest | ItemFlags.Bank | ItemFlags.Temp, 12, 99),
            part.Items[1]);
        Assert.Equal(ItemFlags.None, part.Items[2].Flags);

        var chair = Assert.Single(part.HouseItems);
        Assert.Equal(new OwnedItem("Fictional Chair", "House Item", 1, 1, 1, ItemFlags.House, 13, null), chair);
    }

    private static (int, string, int, int, int, int) Tuple(AqwCompanion.Core.Reputation.FactionStanding f) =>
        (f.FactionId, f.Name, f.Rank, f.Rep, f.RepToRank, f.SpillRep);

    [Fact]
    public void Numbers_and_flags_may_be_numbers_strings_or_booleans()
    {
        var item = new Dictionary<string, object?>
        {
            ["ItemID"] = 7,
            ["sName"] = "Typed Item",
            ["sType"] = "Item",
            ["iQty"] = "4",
            ["iStk"] = 9,
            ["iLvl"] = "12",
            ["bCoins"] = true,
            ["bUpg"] = "1",
            ["bEquip"] = 1,
            ["bWear"] = false,
            ["bQuest"] = "0",
            ["bTemp"] = "true",
        };
        var faction = new Dictionary<string, object?> { ["FactionID"] = 3, ["sName"] = "Typed Faction", ["iRep"] = 900 };

        var part = Inventory(LoadInventoryBig([faction], [item]));

        Assert.Equal(2, Assert.Single(part.Factions).Rank);
        var parsed = Assert.Single(part.Items);
        Assert.Equal((4, 9, 12, 7), (parsed.Quantity, parsed.Stack, parsed.Level, parsed.ItemId));
        Assert.Equal(ItemFlags.Coins | ItemFlags.Upgrade | ItemFlags.Equipped | ItemFlags.Temp, parsed.Flags);
    }

    [Fact]
    public void Invalid_entries_are_skipped_and_duplicates_dropped()
    {
        var part = Inventory(LoadInventoryBig(
            [
                Faction(1, "Good", 100),
                Faction(1, "Duplicate Id", 999),
                Faction(-2, "Negative Id", 100),
                Faction(3, "", 100),
                Faction(4, "Negative Rep", -5),
                new Dictionary<string, object?> { ["FactionID"] = "x", ["sName"] = "Junk Id", ["iRep"] = "1" },
                new Dictionary<string, object?> { ["FactionID"] = "5", ["sName"] = "No Rep" },
                "not an object",
                null,
                Faction(6, "Also Good", 0),
            ],
            [
                Item("Good Item"),
                Item(""),
                Item("   "),
                "junk",
                42,
                Item("Negative Numbers", qty: -3, stack: -1, level: -9),
            ]));

        Assert.Equal(["Good", "Also Good"], part.Factions.Select(f => f.Name));
        Assert.Equal(100, part.Factions[0].Rep);
        Assert.Equal(["Good Item", "Negative Numbers"], part.Items.Select(i => i.Name));
        var clamped = part.Items[1];
        Assert.Equal((0, 0, 0), (clamped.Quantity, clamped.Stack, clamped.Level));
    }

    [Fact]
    public void Text_fields_are_cleaned_and_capped()
    {
        var part = Inventory(LoadInventoryBig(
            [Faction(1, "  Spaced\u0007Name  " + new string('f', 300), 1)],
            [Item(new string('n', 500) + "\n", new string('t', 100))]));

        Assert.Equal(200, part.Factions[0].Name.Length);
        Assert.StartsWith("SpacedName", part.Factions[0].Name);
        Assert.Equal(200, part.Items[0].Name.Length);
        Assert.Equal(40, part.Items[0].Type.Length);
    }

    private static Dictionary<string, object?> Slim(string name) => new() { ["sName"] = name };

    [Fact]
    public void More_than_the_allowed_factions_and_items_are_truncated_not_fatal()
    {
        var factions = Enumerable.Range(0, 600).Select(i => (object?)Faction(i, $"Faction {i}", i));
        var items = Enumerable.Range(0, 5200).Select(i => (object?)Slim($"Item {i}"));
        var houseItems = Enumerable.Range(0, 5200).Select(i => (object?)Slim($"House {i}"));

        var part = Inventory(LoadInventoryBig(factions, items, houseItems));

        Assert.Equal(500, part.Factions.Count);
        Assert.Equal(5000, part.Items.Count);
        Assert.Equal(5000, part.HouseItems.Count);
        Assert.Equal("Item 4999", part.Items[^1].Name);
    }

    [Fact]
    public void A_loadInventoryBig_without_any_list_gives_nothing()
    {
        Assert.Null(Extract(Envelope("loadInventoryBig", new Dictionary<string, object?> { ["bankCount"] = 1 })));
    }

    [Fact]
    public void Every_numeric_profile_field_is_extracted()
    {
        var profile = Profile(InitUserData());

        Assert.Equal(
            new CharacterProfile("Fictional Hero", 60, 123456, 654321, 9876543210L, 777, 5000, 300, 90, 80, 70, 5, true, 25, 10, 20, 30, 40),
            profile);
    }

    [Fact]
    public void Profile_numbers_may_be_strings_and_a_missing_membership_is_not_member()
    {
        var data = ProfileData();
        data["intGold"] = "1500";
        data["iUpg"] = "0";
        data["iUpgDays"] = -12;
        data.Remove("iBoostRep");

        var profile = Profile(InitUserData(data));

        Assert.Equal(1500, profile.Gold);
        Assert.False(profile.IsMember);
        Assert.Equal(-12, profile.MembershipDays);
        Assert.Equal(0, profile.BoostReputation);
    }

    [Fact]
    public void The_profile_of_another_player_without_gold_is_ignored()
    {
        Assert.Null(Extract(InitUserData(ProfileData("Someone Else", own: false))));
    }

    [Fact]
    public void A_profile_with_gold_but_an_empty_name_is_ignored()
    {
        var data = ProfileData("   ");

        Assert.Null(Extract(InitUserData(data)));
    }

    [Fact]
    public void A_profile_without_a_data_object_is_ignored()
    {
        Assert.Null(Extract(Envelope("initUserData", new Dictionary<string, object?> { ["data"] = 5 })));
    }

    [Fact]
    public void Unknown_commands_give_nothing()
    {
        Assert.Null(Extract(Envelope("initUserDatas", new Dictionary<string, object?> { ["a"] = new List<object?>() })));
        Assert.Null(Extract(Envelope("loadShop")));
    }

    [Fact]
    public void Privacy_inventory_result_contains_no_marker_secret()
    {
        var body = LoadInventoryBig(
            [Faction(1, "Fictional Order", 10)],
            [Item("Fictional Blade", "Sword"), Item("Other", coins: true)],
            [Item("Chair")]);
        ((Dictionary<string, object?>)((Dictionary<string, object?>)body["b"]!)["o"]!)["strEmail"] = SecretEmail;
        ((Dictionary<string, object?>)((Dictionary<string, object?>)body["b"]!)["o"]!)["extra"] = new[] { SecretPlayer, SecretToken, SecretIp };

        var part = Extract(body);

        Assert.NotNull(part);
        AssertNoSecrets(part);
    }

    [Fact]
    public void Privacy_profile_result_contains_no_marker_secret()
    {
        var part = Extract(InitUserData());

        Assert.NotNull(part);
        AssertNoSecrets(part);
    }

    [Fact]
    public void The_privacy_scan_does_detect_a_leaked_marker()
    {
        var leaked = new ProfilePart(Profile(InitUserData()) with { Name = "leak " + SecretEmail });

        Assert.ThrowsAny<Exception>(() => AssertNoSecrets(leaked));
    }

    [Fact]
    public void Inventory_marker_in_an_allowed_text_field_is_caught_by_the_scan()
    {
        var part = Inventory(LoadInventoryBig([], [Item("Item " + SecretToken)]));

        Assert.ThrowsAny<Exception>(() => AssertNoSecrets(part));
    }

    [Fact]
    public void Json_serialization_of_the_result_has_only_allow_listed_names()
    {
        var part = Inventory(LoadInventoryBig([Faction(1, "F", 1)], [Item("I")], [Item("H")]));

        var json = JsonSerializer.Serialize(part);

        Assert.DoesNotContain("sDesc", json);
        Assert.DoesNotContain("tPurchase", json);
        Assert.DoesNotContain("metaValues", json);
        Assert.DoesNotContain("CharItemID", json);
    }
}
