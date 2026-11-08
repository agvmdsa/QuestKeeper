namespace AqwCompanion.Core.Capture;

/// <summary>
/// One item the Character owns, with only the Allow-listed fields. <paramref name="CharItemId"/> is the Character's own id of
/// the item (the game's <c>CharItemID</c>); it lets the live events find the item again, and is 0 when it is not known.
/// </summary>
public sealed record OwnedItem(
    string Name,
    string Type,
    int Quantity,
    int Stack,
    int Level,
    ItemFlags Flags,
    int ItemId,
    int? ClassId,
    int CharItemId = 0);
