using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Reputation;

namespace AqwCompanion.App.GameData;

/// <summary>
/// Maps the captured Characters to one stored value of plain DTOs and validates it on load. Failures never reach the caller:
/// the stored data module swallows and reports them.
/// </summary>
public sealed class GameDataStore(StoredData data) : IGameDataStore
{
    /// <summary>The most Characters kept.</summary>
    public const int MaxCharacters = 16;

    /// <summary>The most items kept per list (owned items, house items).</summary>
    public const int MaxItems = 5000;

    private const int KnownFlags = 255;

    public sealed record GameDataDto(List<CharacterDto> Characters);

    public sealed record CharacterDto(
        ProfileDto Profile,
        List<FactionDto> Factions,
        List<ItemDto> Items,
        List<ItemDto> HouseItems,
        DateTimeOffset CapturedAtUtc);

    public sealed record ProfileDto(
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

    public sealed record FactionDto(int FactionId, string Name, int Rank, int Rep, int RepToRank, int SpillRep);

    public sealed record ItemDto(string Name, string Type, int Quantity, int Stack, int Level, int Flags, int ItemId, int? ClassId);

    public IReadOnlyList<CharacterSnapshot> Load()
    {
        var stored = data.Load(StoredKeys.GameData);
        if (stored?.Value?.Characters is not { } dtos)
        {
            return [];
        }

        var characters = new List<CharacterSnapshot>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            if (characters.Count >= MaxCharacters)
            {
                break;
            }

            if (ToSnapshot(dto) is { } snapshot && names.Add(snapshot.Profile.Name))
            {
                characters.Add(snapshot);
            }
        }

        return characters;
    }

    public void Save(IReadOnlyList<CharacterSnapshot> characters)
    {
        if (characters.Count == 0)
        {
            data.Remove(StoredKeys.GameData);
            return;
        }

        data.Save(
            StoredKeys.GameData,
            new GameDataDto(characters.Select(ToDto).ToList()),
            characters.Max(c => c.CapturedAtUtc));
    }

    private static CharacterDto ToDto(CharacterSnapshot c)
    {
        var p = c.Profile;
        return new CharacterDto(
            new ProfileDto(
                p.Name, p.Level, p.Exp, p.ExpToLevel, p.Gold, p.Coins, p.ClassPoints, p.DaysPlayed, p.BagSlots, p.BankSlots,
                p.HouseSlots, p.LoadoutSlots, p.IsMember, p.MembershipDays, p.BoostXp, p.BoostGold, p.BoostClassPoints, p.BoostReputation),
            c.Factions.Select(f => new FactionDto(f.FactionId, f.Name, f.Rank, f.Rep, f.RepToRank, f.SpillRep)).ToList(),
            c.Items.Select(ToDto).ToList(),
            c.HouseItems.Select(ToDto).ToList(),
            c.CapturedAtUtc);
    }

    private static ItemDto ToDto(OwnedItem i) =>
        new(i.Name, i.Type, i.Quantity, i.Stack, i.Level, (int)i.Flags, i.ItemId, i.ClassId);

    private static CharacterSnapshot? ToSnapshot(CharacterDto? dto)
    {
        if (dto?.Profile is not { } p || string.IsNullOrWhiteSpace(p.Name)
            || p.Level < 0 || p.Exp < 0 || p.ExpToLevel < 0 || p.Gold < 0 || p.Coins < 0 || p.ClassPoints < 0 || p.DaysPlayed < 0
            || p.BagSlots < 0 || p.BankSlots < 0 || p.HouseSlots < 0 || p.LoadoutSlots < 0 || p.MembershipDays < 0
            || p.BoostXp < 0 || p.BoostGold < 0 || p.BoostClassPoints < 0 || p.BoostReputation < 0)
        {
            return null;
        }

        var profile = new CharacterProfile(
            p.Name, p.Level, p.Exp, p.ExpToLevel, p.Gold, p.Coins, p.ClassPoints, p.DaysPlayed, p.BagSlots, p.BankSlots,
            p.HouseSlots, p.LoadoutSlots, p.IsMember, p.MembershipDays, p.BoostXp, p.BoostGold, p.BoostClassPoints, p.BoostReputation);
        var factions = (dto.Factions ?? [])
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name)
                && f.FactionId >= 0 && f.Rank >= 0 && f.Rep >= 0 && f.RepToRank >= 0 && f.SpillRep >= 0)
            .DistinctBy(f => f.FactionId)
            .Select(f => new FactionStanding(f.FactionId, f.Name, f.Rank, f.Rep, f.RepToRank, f.SpillRep))
            .ToList();
        return new CharacterSnapshot(profile, factions, ToItems(dto.Items), ToItems(dto.HouseItems), dto.CapturedAtUtc);
    }

    private static List<OwnedItem> ToItems(List<ItemDto>? dtos) =>
        (dtos ?? [])
            .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Name)
                && i.Quantity >= 0 && i.Stack >= 0 && i.Level >= 0 && i.ItemId >= 0 && i.ClassId is null or >= 0)
            .Take(MaxItems)
            .Select(i => new OwnedItem(i.Name, i.Type ?? string.Empty, i.Quantity, i.Stack, i.Level, (ItemFlags)(i.Flags & KnownFlags), i.ItemId, i.ClassId))
            .ToList();
}
