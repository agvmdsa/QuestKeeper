using System.Text.Json;
using AqwCompanion.Core.Common;

namespace AqwCompanion.Core.Capture;

/// <summary>
/// What applying one event did. <see cref="Outcome"/> is one of the fixed words <see cref="LiveUpdates.Applied"/>,
/// <see cref="LiveUpdates.Ignored"/>, <see cref="LiveUpdates.Unmatched"/> or <see cref="LiveUpdates.Cached"/>, never any content.
/// <see cref="Snapshot"/> is the given snapshot itself when nothing changed.
/// </summary>
public sealed record LiveUpdateResult(CharacterSnapshot Snapshot, bool Changed, string Outcome);

/// <summary>
/// Applies the game's own events (gold and exp gained, items picked up, sold or changed, class points) to a snapshot. Pure: the
/// snapshot records are never mutated, a changed copy comes back. Only the fields named here are read from the message; every
/// other field of the event is never looked at, so nothing else can reach the result. Buying, quest rewards, reputation, level-ups,
/// trades and bank moves are not handled yet, so the snapshot can drift until the next login.
/// </summary>
public static class LiveUpdates
{
    /// <summary>
    /// ASSUMPTION, not yet confirmed against the game: <c>addGoldExp</c> carries the amounts GAINED by the event (deltas), not the
    /// new totals. The profile gold and exp are increased by them. The Game data tab shows the last amounts so the player can compare
    /// them with the game; if they turn out to be totals, this is the single place to change.
    /// </summary>
    public const bool GoldExpEventsCarryGains = true;

    /// <summary>The event changed (or confirmed) the snapshot.</summary>
    public const string Applied = "applied";

    /// <summary>The event is not one this module handles, lacks the fields it needs, or does not apply.</summary>
    public const string Ignored = "ignored";

    /// <summary>The event names an item the snapshot does not hold and no cached definition explains.</summary>
    public const string Unmatched = "unmatched";

    /// <summary>The event only taught the cache a dropped item definition.</summary>
    public const string Cached = "cached";

    /// <summary>The most entries of one event that are read.</summary>
    public const int MaxEntriesPerEvent = 64;

    private const ItemFlags DropFlags = ItemFlags.Coins | ItemFlags.Upgrade | ItemFlags.Quest | ItemFlags.Temp;

    /// <summary>The only commands the live reading opens.</summary>
    public static IReadOnlyList<string> Commands { get; } = ["addGoldExp", "addItems", "dropItem", "getDrop", "sellItem", "updateClass"];

    /// <summary>True when the command is one of <see cref="Commands"/>.</summary>
    public static bool IsLiveCommand(string? name) => name is not null && Commands.Contains(name);

    /// <summary>Applies one event. Never throws; an event that cannot be applied gives <see cref="Ignored"/> or <see cref="Unmatched"/> and the same snapshot.</summary>
    public static LiveUpdateResult Apply(CharacterSnapshot snapshot, GameMessage message, LiveEventCache cache)
    {
        try
        {
            if (!message.Document.RootElement.TryGetProperty("b", out var b)
                || b.ValueKind != JsonValueKind.Object
                || !b.TryGetProperty("o", out var o)
                || o.ValueKind != JsonValueKind.Object)
            {
                return Same(snapshot, Ignored);
            }

            return message.Command switch
            {
                "addGoldExp" => GoldExp(snapshot, o),
                "addItems" => AddItems(snapshot, o),
                "dropItem" => DropItem(snapshot, o, cache),
                "getDrop" => GetDrop(snapshot, o, cache),
                "sellItem" => SellItem(snapshot, o),
                "updateClass" => UpdateClass(snapshot, o),
                _ => Same(snapshot, Ignored),
            };
        }
        catch (Exception)
        {
            // No content may leave this module, not even in an exception.
            return Same(snapshot, Ignored);
        }
    }

    private static LiveUpdateResult GoldExp(CharacterSnapshot snapshot, JsonElement o)
    {
        var gold = o.ReadLong("intGold");
        var exp = o.ReadLong("intExp");
        if (gold is null && exp is null)
        {
            return Same(snapshot, Ignored);
        }

        var profile = snapshot.Profile with
        {
            Gold = Add(snapshot.Profile.Gold, gold ?? 0),
            Exp = Add(snapshot.Profile.Exp, exp ?? 0),
        };
        return Result(snapshot, snapshot with { Profile = profile });
    }

    private static LiveUpdateResult AddItems(CharacterSnapshot snapshot, JsonElement o)
    {
        if (!o.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
        {
            return Same(snapshot, Ignored);
        }

        List<OwnedItem>? edited = null;
        var matched = false;
        var visited = 0;
        foreach (var property in items.EnumerateObject())
        {
            if (++visited > MaxEntriesPerEvent)
            {
                break;
            }

            var id = property.Value.ReadLong("CharItemID") ?? (long.TryParse(property.Name, out var keyed) ? keyed : 0);
            var quantity = property.Value.ReadLong("iQtyNow");
            var index = IndexOf(edited ?? snapshot.Items, id);
            if (quantity is null || index < 0)
            {
                continue;
            }

            edited ??= [.. snapshot.Items];
            edited[index] = edited[index] with { Quantity = Quantity(quantity.Value) };
            matched = true;
        }

        return matched ? Result(snapshot, snapshot with { Items = edited! }) : Same(snapshot, Unmatched);
    }

    private static LiveUpdateResult DropItem(CharacterSnapshot snapshot, JsonElement o, LiveEventCache cache)
    {
        if (!o.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
        {
            return Same(snapshot, Ignored);
        }

        var cached = false;
        var visited = 0;
        foreach (var property in items.EnumerateObject())
        {
            if (++visited > MaxEntriesPerEvent)
            {
                break;
            }

            if (GameDataExtractor.ReadItem(property.Value, ItemFlags.None) is not { } definition)
            {
                continue;
            }

            var itemId = definition.ItemId > 0 ? definition.ItemId : int.TryParse(property.Name, out var keyed) && keyed > 0 ? keyed : 0;
            if (itemId == 0)
            {
                continue;
            }

            cache.Store(definition with { ItemId = itemId, CharItemId = 0, Flags = definition.Flags & DropFlags });
            cached = true;
        }

        return Same(snapshot, cached ? Cached : Ignored);
    }

    private static LiveUpdateResult GetDrop(CharacterSnapshot snapshot, JsonElement o, LiveEventCache cache)
    {
        var quantity = o.ReadLong("iQtyNow") ?? o.ReadLong("iQty");
        if (o.ReadFlag("bSuccess") != true || quantity is null)
        {
            return Same(snapshot, Ignored);
        }

        var charItemId = o.ReadLong("CharItemID") ?? 0;
        var index = IndexOf(snapshot.Items, charItemId);
        if (index >= 0)
        {
            var edited = new List<OwnedItem>(snapshot.Items);
            edited[index] = edited[index] with { Quantity = Quantity(quantity.Value) };
            return Result(snapshot, snapshot with { Items = edited });
        }

        var itemId = o.ReadLong("ItemID") ?? 0;
        if (itemId <= 0 || itemId > int.MaxValue || !cache.TryGet((int)itemId, out var definition))
        {
            return Same(snapshot, Unmatched);
        }

        if (snapshot.Items.Count >= GameDataExtractor.MaxItems)
        {
            return Same(snapshot, Ignored);
        }

        var created = definition with
        {
            Quantity = Quantity(quantity.Value),
            CharItemId = charItemId is > 0 and <= int.MaxValue ? (int)charItemId : 0,
            Flags = definition.Flags | (o.ReadFlag("bBank") == true ? ItemFlags.Bank : ItemFlags.None),
        };
        return Result(snapshot, snapshot with { Items = [.. snapshot.Items, created] });
    }

    private static LiveUpdateResult SellItem(CharacterSnapshot snapshot, JsonElement o)
    {
        var amount = Math.Max(0, o.ReadLong("intAmount") ?? 0);
        var coins = o.ReadFlag("bCoins") == true;
        var index = IndexOf(snapshot.Items, o.ReadLong("CharItemID") ?? 0);
        if (index < 0 && amount == 0)
        {
            return Same(snapshot, Unmatched);
        }

        var items = snapshot.Items;
        if (index >= 0 && o.ReadLong("iQtyNow") is { } quantityNow)
        {
            var edited = new List<OwnedItem>(snapshot.Items);
            if (quantityNow <= 0)
            {
                edited.RemoveAt(index);
            }
            else
            {
                edited[index] = edited[index] with { Quantity = Quantity(quantityNow) };
            }

            items = edited;
        }

        var profile = coins
            ? snapshot.Profile with { Coins = SafeInt.Clamp(Add(snapshot.Profile.Coins, amount)) }
            : snapshot.Profile with { Gold = Add(snapshot.Profile.Gold, amount) };
        return Result(snapshot, snapshot with { Profile = profile, Items = items });
    }

    private static LiveUpdateResult UpdateClass(CharacterSnapshot snapshot, JsonElement o)
    {
        if (o.ReadLong("iCP") is not { } points)
        {
            return Same(snapshot, Ignored);
        }

        return Result(snapshot, snapshot with { Profile = snapshot.Profile with { ClassPoints = Quantity(points) } });
    }

    private static int IndexOf(IReadOnlyList<OwnedItem> items, long charItemId)
    {
        if (charItemId <= 0)
        {
            return -1;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].CharItemId == charItemId)
            {
                return i;
            }
        }

        return -1;
    }

    private static int Quantity(long value) => SafeInt.Clamp(Math.Max(0, value));

    // Adds a gain to a total without overflow; the result is never below zero.
    private static long Add(long total, long delta)
    {
        if (delta > 0 && total > long.MaxValue - delta)
        {
            return long.MaxValue;
        }

        return Math.Max(0, total + delta);
    }

    private static LiveUpdateResult Result(CharacterSnapshot before, CharacterSnapshot after)
    {
        var changed = after.Profile != before.Profile || !ReferenceEquals(after.Items, before.Items);
        return new LiveUpdateResult(changed ? after : before, changed, Applied);
    }

    private static LiveUpdateResult Same(CharacterSnapshot snapshot, string outcome) => new(snapshot, false, outcome);
}
