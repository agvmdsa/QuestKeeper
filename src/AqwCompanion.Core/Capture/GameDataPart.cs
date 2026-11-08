using AqwCompanion.Core.Reputation;

namespace AqwCompanion.Core.Capture;

/// <summary>One Allow-listed part of a Character's data, read from a single game message.</summary>
public abstract record GameDataPart;

/// <summary>The own profile numbers, read from <c>initUserData</c>.</summary>
public sealed record ProfilePart(CharacterProfile Profile) : GameDataPart;

/// <summary>Faction standings, owned items and house items, read from <c>loadInventoryBig</c>.</summary>
public sealed record InventoryPart(
    IReadOnlyList<FactionStanding> Factions,
    IReadOnlyList<OwnedItem> Items,
    IReadOnlyList<OwnedItem> HouseItems) : GameDataPart;
