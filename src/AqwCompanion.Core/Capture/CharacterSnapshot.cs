using AqwCompanion.Core.Reputation;

namespace AqwCompanion.Core.Capture;

/// <summary>The Game data of one Character read from the game traffic at one moment.</summary>
public sealed record CharacterSnapshot(
    CharacterProfile Profile,
    IReadOnlyList<FactionStanding> Factions,
    IReadOnlyList<OwnedItem> Items,
    IReadOnlyList<OwnedItem> HouseItems,
    DateTimeOffset CapturedAtUtc);
