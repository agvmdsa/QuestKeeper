namespace AqwCompanion.Core.Capture;

/// <summary>The yes/no properties of an owned item that the Allow list keeps.</summary>
[Flags]
public enum ItemFlags
{
    None = 0,
    Coins = 1,
    Upgrade = 2,
    Equipped = 4,
    Worn = 8,
    Quest = 16,
    Bank = 32,
    House = 64,
    Temp = 128,
}
