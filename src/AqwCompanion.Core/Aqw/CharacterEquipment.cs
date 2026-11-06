using System.Globalization;

namespace AqwCompanion.Core.Aqw;

/// <summary>One equipped item. <see cref="IsCustom"/> marks the "custom" (cosmetic override) variants.</summary>
public sealed record EquipmentSlot(string Label, string ItemName, int? ItemId, bool IsCustom);

/// <summary>One of the six character colors, as a 0xRRGGBB value.</summary>
public sealed record CharacterColor(string Label, int Rgb)
{
    public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{Rgb & 0xFFFFFF:X6}");

    public string Description => $"{Label} color {Hex}";
}

/// <summary>
/// What the public character page shows as equipped. Colors are only the ones the page provided.
/// <see cref="CharacterId"/> is the numeric id the page uses to load the badges.
/// </summary>
public sealed record CharacterEquipment(
    string? Name,
    int? Level,
    string? Faction,
    string? Guild,
    string? Gender,
    string? ClassName,
    string? HairName,
    IReadOnlyList<EquipmentSlot> Slots,
    IReadOnlyList<CharacterColor> Colors,
    int? CharacterId = null)
{
    public IEnumerable<EquipmentSlot> StandardSlots => Slots.Where(slot => !slot.IsCustom);

    public IEnumerable<EquipmentSlot> CustomSlots => Slots.Where(slot => slot.IsCustom);

    public bool HasCustomSlots => Slots.Any(slot => slot.IsCustom);

    public bool HasColors => Colors.Count > 0;

    public string Title => string.IsNullOrWhiteSpace(Name) ? "Character" : Name;

    public string Summary
    {
        get
        {
            var parts = new List<string>(3);
            if (Level is { } level)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"Level {level}"));
            }

            if (!string.IsNullOrWhiteSpace(Faction))
            {
                parts.Add(Faction);
            }

            if (!string.IsNullOrWhiteSpace(Guild))
            {
                parts.Add($"Guild: {Guild}");
            }

            return string.Join(" | ", parts);
        }
    }
}
