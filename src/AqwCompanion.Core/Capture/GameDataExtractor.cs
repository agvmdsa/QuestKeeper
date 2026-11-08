using System.Text.Json;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Reputation;

namespace AqwCompanion.Core.Capture;

/// <summary>
/// The Allow list. Reads the few fields the app may keep out of a decoded game message and nothing else: every other field
/// (e-mail, IP, ids, dates, descriptions, other players) is never copied, so it cannot be stored, logged or shown.
/// </summary>
public static class GameDataExtractor
{
    public const int MaxFactions = 500;
    public const int MaxItems = 5000;
    public const int MaxNameLength = 200;
    public const int MaxTypeLength = 40;
    public const int MaxCharacterNameLength = 60;

    private const string InventoryCommand = "loadInventoryBig";
    private const string ProfileCommand = "initUserData";

    /// <summary>
    /// loadInventoryBig gives an InventoryPart; initUserData gives a ProfilePart only when b.o.data has intGold AND a non-empty
    /// strUsername (other players' profiles lack intGold); anything else gives null. Never throws.
    /// </summary>
    public static GameDataPart? Extract(GameMessage message)
    {
        try
        {
            var root = message.Document.RootElement;
            if (!root.TryGetProperty("b", out var b)
                || b.ValueKind != JsonValueKind.Object
                || !b.TryGetProperty("o", out var o)
                || o.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return message.Command switch
            {
                InventoryCommand => ExtractInventory(o),
                ProfileCommand => ExtractProfile(o),
                _ => null,
            };
        }
        catch (Exception)
        {
            // No content may leave the extractor, not even in an exception.
            return null;
        }
    }

    private static ProfilePart? ExtractProfile(JsonElement o)
    {
        if (!o.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var gold = data.ReadLong("intGold");
        var name = data.ReadCleanText("strUsername", MaxCharacterNameLength);
        if (gold is null || name is null)
        {
            return null;
        }

        var profile = new CharacterProfile(
            name,
            Int(data, "intLevel"),
            Long(data, "intExp"),
            Long(data, "intExpToLevel"),
            Math.Max(0, gold.Value),
            Int(data, "intCoins"),
            Int(data, "iCP"),
            Int(data, "intDaysPlayed"),
            Int(data, "iBagSlots"),
            Int(data, "iBankSlots"),
            Int(data, "iHouseSlots"),
            Int(data, "iLoadoutSlots"),
            data.ReadFlag("iUpg") ?? false,
            SafeInt.Clamp(data.ReadLong("iUpgDays") ?? 0),
            Int(data, "iBoostXP"),
            Int(data, "iBoostG"),
            Int(data, "iBoostCP"),
            Int(data, "iBoostRep"));
        return new ProfilePart(profile);
    }

    private static int Int(JsonElement data, string property) => SafeInt.Clamp(Math.Max(0, data.ReadLong(property) ?? 0));

    private static long Long(JsonElement data, string property) => Math.Max(0, data.ReadLong(property) ?? 0);

    private static InventoryPart? ExtractInventory(JsonElement o)
    {
        var hasFactions = o.TryGetProperty("factions", out var factions) && factions.ValueKind == JsonValueKind.Array;
        var hasItems = o.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array;
        var hasHouseItems = o.TryGetProperty("hitems", out var houseItems) && houseItems.ValueKind == JsonValueKind.Array;
        if (!hasFactions && !hasItems && !hasHouseItems)
        {
            return null;
        }

        return new InventoryPart(
            hasFactions ? ReadFactions(factions) : [],
            hasItems ? ReadItems(items, ItemFlags.None) : [],
            hasHouseItems ? ReadItems(houseItems, ItemFlags.House) : []);
    }

    private static List<FactionStanding> ReadFactions(JsonElement array)
    {
        var result = new List<FactionStanding>();
        var seen = new HashSet<int>();
        var visited = 0;
        foreach (var entry in array.EnumerateArray())
        {
            if (++visited > MaxFactions)
            {
                break;
            }

            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = entry.ReadLong("FactionID");
            var name = entry.ReadCleanText("sName", MaxNameLength);
            var rep = entry.ReadLong("iRep");
            if (id is null or < 0 or > int.MaxValue || name is null || rep is null or < 0 || !seen.Add((int)id.Value))
            {
                continue;
            }

            result.Add(ReputationRank.Standing((int)id.Value, name, rep.Value));
        }
        return result;
    }

    private static List<OwnedItem> ReadItems(JsonElement array, ItemFlags always)
    {
        var result = new List<OwnedItem>();
        var visited = 0;
        foreach (var entry in array.EnumerateArray())
        {
            if (++visited > MaxItems)
            {
                break;
            }

            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = entry.ReadCleanText("sName", MaxNameLength);
            if (name is null)
            {
                continue;
            }

            var classId = entry.ReadLong("ClassID");
            result.Add(new OwnedItem(
                name,
                entry.ReadCleanText("sType", MaxTypeLength) ?? string.Empty,
                Int(entry, "iQty"),
                Int(entry, "iStk"),
                Int(entry, "iLvl"),
                always | ReadFlags(entry),
                Int(entry, "ItemID"),
                classId is > 0 and <= int.MaxValue ? (int)classId.Value : null));
        }
        return result;
    }

    private static ItemFlags ReadFlags(JsonElement entry)
    {
        var flags = ItemFlags.None;
        flags |= Flag(entry, "bCoins", ItemFlags.Coins);
        flags |= Flag(entry, "bUpg", ItemFlags.Upgrade);
        flags |= Flag(entry, "bEquip", ItemFlags.Equipped);
        flags |= Flag(entry, "bWear", ItemFlags.Worn);
        flags |= Flag(entry, "bQuest", ItemFlags.Quest);
        flags |= Flag(entry, "bBank", ItemFlags.Bank);
        flags |= Flag(entry, "bHouse", ItemFlags.House);
        flags |= Flag(entry, "bTemp", ItemFlags.Temp);
        return flags;
    }

    private static ItemFlags Flag(JsonElement entry, string property, ItemFlags flag) =>
        entry.ReadFlag(property) == true ? flag : ItemFlags.None;
}
