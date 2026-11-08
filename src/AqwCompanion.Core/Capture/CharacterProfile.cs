namespace AqwCompanion.Core.Capture;

/// <summary>The profile numbers of the Character, with only the Allow-listed fields.</summary>
public sealed record CharacterProfile(
    string Name,
    int Level,
    long Exp,
    long ExpToLevel,
    long Gold,
    int Coins,
    int ClassPoints,
    int DaysPlayed,
    int BagSlots,
    int BankSlots,
    int HouseSlots,
    int LoadoutSlots,
    bool IsMember,
    int MembershipDays,
    int BoostXp,
    int BoostGold,
    int BoostClassPoints,
    int BoostReputation);
