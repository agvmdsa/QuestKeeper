using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Site;

namespace AqwCompanion.Core.Equipment;

/// <summary>
/// Reads the equipped items from the public character page. The data lives in the <c>FlashVars</c> of the
/// embedded character viewer. Parsing never throws and nothing parsed is logged.
/// </summary>
public sealed partial class CharacterEquipmentParser : IPageParser<CharacterEquipment>
{
    public const string FlashVarsParamMarker = "param[name=FlashVars]";
    public const string FlashVarsEmbedMarker = "embed[flashvars]";

    private const string ParamStrategy = "flashvars-param";
    private const string EmbedStrategy = "flashvars-embed";
    private const int MaxValueLength = 200;

    [GeneratedRegex(@"\bvar\s+ccid\s*=\s*(\d{1,9})\s*;", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CharacterIdPattern();

    private static readonly (string Label, string Key, string IdKey)[] Slots =
    [
        ("Armor", "strArmorName", "ArmorID"),
        ("Weapon", "strWeaponName", "WeaponID"),
        ("Cape", "strCapeName", "CapeID"),
        ("Helm", "strHelmName", "HelmID"),
        ("Pet", "strPetName", "PetID"),
        ("Misc", "strMiscName", "MiscID"),
    ];

    private static readonly string[] CustomParts = ["Weapon", "Armor", "Cape", "Helm", "Pet", "Misc"];

    private static readonly (string Label, string Key)[] Colors =
    [
        ("Hair", "intColorHair"),
        ("Skin", "intColorSkin"),
        ("Eye", "intColorEye"),
        ("Trim", "intColorTrim"),
        ("Base", "intColorBase"),
        ("Accessory", "intColorAccessory"),
    ];

    public string Name => "character-equipment";
    public string Version => "1";

    public bool CanParse(PageSnapshot snapshot) => AqwUrls.IsCharPage(snapshot.Url);

    public ParseResult<CharacterEquipment> Parse(PageSnapshot snapshot)
    {
        var document = new HtmlParser().ParseDocument(snapshot.Payload);
        var (flashVars, strategy) = FindFlashVars(document);

        if (flashVars is null)
        {
            return Failed("character viewer data not found", [FlashVarsParamMarker, FlashVarsEmbedMarker]);
        }

        var values = ParsePairs(flashVars);
        var slots = ReadSlots(values);
        if (slots.Count == 0)
        {
            return Failed("no equipped items found", ["strArmorName", "strWeaponName"]);
        }

        var equipment = new CharacterEquipment(
            Get(values, "strName"),
            ReadLevel(values),
            Get(values, "strFaction"),
            Get(values, "guild"),
            Get(values, "strGender"),
            Get(values, "strClassName"),
            Get(values, "strHairName"),
            slots,
            ReadColors(values),
            ReadCharacterId(document));

        return ParseResult<CharacterEquipment>.Ok(
            equipment,
            new ParseDiagnostics(Name, Version, strategy, [], slots.Count));
    }

    private ParseResult<CharacterEquipment> Failed(string reason, IReadOnlyList<string> missing) =>
        ParseResult<CharacterEquipment>.Failed(reason, new ParseDiagnostics(Name, Version, null, missing, 0));

    // The page loads its badges and inventory with the numeric id set in an inline script: "var ccid = 3;".
    private static int? ReadCharacterId(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script"))
        {
            var match = CharacterIdPattern().Match(script.TextContent);
            if (match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id > 0)
            {
                return id;
            }
        }

        return null;
    }

    private static (string? Value, string? Strategy) FindFlashVars(IDocument document)
    {
        foreach (var param in document.QuerySelectorAll("param"))
        {
            if (string.Equals(param.GetAttribute("name"), "FlashVars", StringComparison.OrdinalIgnoreCase)
                && param.GetAttribute("value") is { Length: > 0 } fromParam)
            {
                return (fromParam, ParamStrategy);
            }
        }

        foreach (var embed in document.QuerySelectorAll("embed"))
        {
            if (embed.GetAttribute("flashvars") is { Length: > 0 } fromEmbed)
            {
                return (fromEmbed, EmbedStrategy);
            }
        }

        return (null, null);
    }

    // The HTML parser has already decoded the attribute entities, so the value is plain "&a=b&c=d".
    private static Dictionary<string, string> ParsePairs(string flashVars)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in flashVars.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                values[pair[..separator].Trim()] = Clean(pair[(separator + 1)..]);
            }
        }

        return values;
    }

    private static string Clean(string raw)
    {
        var value = raw.Contains('%', StringComparison.Ordinal) ? Uri.UnescapeDataString(raw) : raw;
        value = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return value.Length > MaxValueLength ? value[..MaxValueLength] : value;
    }

    private static List<EquipmentSlot> ReadSlots(Dictionary<string, string> values)
    {
        var slots = new List<EquipmentSlot>();
        Add(slots, "Class", Get(values, "strClassName"), null, false);

        foreach (var (label, key, idKey) in Slots)
        {
            Add(slots, label, Get(values, key), ReadId(values, idKey), false);
        }

        foreach (var part in CustomParts)
        {
            Add(slots, $"Custom {part.ToLowerInvariant()}", Get(values, $"strCust{part}Name"), ReadId(values, $"Cust{part}ID"), true);
        }

        return slots;
    }

    private static void Add(List<EquipmentSlot> slots, string label, string? item, int? id, bool isCustom)
    {
        if (item is not null)
        {
            slots.Add(new EquipmentSlot(label, item, id, isCustom));
        }
    }

    private static List<CharacterColor> ReadColors(Dictionary<string, string> values)
    {
        var colors = new List<CharacterColor>(Colors.Length);
        foreach (var (label, key) in Colors)
        {
            if (Get(values, key) is { } text
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var rgb))
            {
                colors.Add(new CharacterColor(label, rgb & 0xFFFFFF));
            }
        }

        return colors;
    }

    private static int? ReadLevel(Dictionary<string, string> values) =>
        ReadPositive(values, "intLevel") ?? ReadPositive(values, "level");

    private static int? ReadId(Dictionary<string, string> values, string key) => ReadPositive(values, key);

    private static int? ReadPositive(Dictionary<string, string> values, string key) =>
        Get(values, key) is { } text
        && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number > 0
            ? number
            : null;

    /// <summary>The value, or null when the key is missing, empty or the literal "none".</summary>
    private static string? Get(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
        && value.Length > 0
        && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
            ? value
            : null;
}
