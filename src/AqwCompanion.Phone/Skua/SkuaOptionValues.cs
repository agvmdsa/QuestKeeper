using System.Globalization;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>
/// Option values between the phone and the plugin. The phone always sees and sends values the way the plugin's definitions
/// describe them: booleans <c>True</c>/<c>False</c>, numbers in invariant culture (<c>1.5</c>), enum members as Skua's
/// dialog shows them (spaces for underscores). Values the plugin read from the options file alone (it had no definitions:
/// Skua restarted) are raw: enum names may keep underscores and decimals are in the PC's culture (<c>1,5</c>); they are
/// normalised here against definitions SkuaLink cached. Pure.
/// </summary>
public static class SkuaOptionValues
{
    /// <summary>
    /// Values from the plugin as the phone sees them. <paramref name="raw"/> says the plugin had no definitions, so the
    /// values are as the options file holds them and are normalised against <paramref name="defs"/> (when known).
    /// </summary>
    public static IReadOnlyList<SkuaOptionValue> FromPlugin(
        IReadOnlyList<SkuaOptionDef>? defs, IReadOnlyList<SkuaOptionValue> values, bool raw, CultureInfo fileCulture)
    {
        if (!raw || defs is null)
        {
            return values;
        }

        var byName = Index(defs);
        return values.Select(v => byName.TryGetValue((v.Category, v.Name), out var def) ? v with { Value = FromFile(def, v.Value, fileCulture) } : v).ToList();
    }

    /// <summary>
    /// The phone's values tidied before they are checked: <c>true</c>/<c>false</c> in any case become <c>True</c>/<c>False</c>
    /// and an enum member written with underscores becomes the member as shown. Everything else is left for
    /// <see cref="SkuaOptionsFile.Validate"/>.
    /// </summary>
    public static IReadOnlyList<SkuaOptionValue> Tidy(IReadOnlyList<SkuaOptionDef> defs, IReadOnlyList<SkuaOptionValue> values)
    {
        var byName = Index(defs);
        return values.Select(v => byName.TryGetValue((v.Category, v.Name), out var def) ? v with { Value = Tidy(def, v.Value) } : v).ToList();
    }

    /// <summary>
    /// Checked values as the plugin must receive them. A plugin with definitions converts doubles for the file itself; one
    /// without (<paramref name="raw"/>) writes the text as it is, so doubles go in the PC's culture, which is how Skua reads
    /// them. Enum members always go as shown (Skua turns spaces back into underscores when it reads the file).
    /// </summary>
    public static IReadOnlyList<SkuaOptionValue> ForPlugin(
        IReadOnlyList<SkuaOptionDef> defs, IReadOnlyList<SkuaOptionValue> values, bool raw, CultureInfo fileCulture)
    {
        if (!raw)
        {
            return values;
        }

        var byName = Index(defs);
        return values.Select(v => byName.TryGetValue((v.Category, v.Name), out var def) && def.Type == "double"
            ? v with { Value = Number(v.Value, CultureInfo.InvariantCulture, fileCulture) }
            : v).ToList();
    }

    private static string FromFile(SkuaOptionDef def, string text, CultureInfo fileCulture) => def.Type switch
    {
        "enum" => text.Replace('_', ' '),
        "double" => Number(text, fileCulture, CultureInfo.InvariantCulture),
        _ => text,
    };

    private static string Tidy(SkuaOptionDef def, string text) => def.Type switch
    {
        "bool" when text.Equals("true", StringComparison.OrdinalIgnoreCase) => "True",
        "bool" when text.Equals("false", StringComparison.OrdinalIgnoreCase) => "False",
        "enum" when def.Choices is { } choices && !choices.Contains(text, StringComparer.Ordinal)
            && choices.Contains(text.Replace('_', ' '), StringComparer.Ordinal) => text.Replace('_', ' '),
        _ => text,
    };

    // The same conversion the plugin uses (SkuaScriptOptions), so a value reads back as it was written.
    private static string Number(string text, CultureInfo from, CultureInfo to) =>
        double.TryParse(text, NumberStyles.Float, from, out var number) && double.IsFinite(number) ? number.ToString(to) : text;

    private static Dictionary<(string, string), SkuaOptionDef> Index(IReadOnlyList<SkuaOptionDef> defs)
    {
        var byName = new Dictionary<(string, string), SkuaOptionDef>();
        foreach (var def in defs)
        {
            byName.TryAdd((def.Category, def.Name), def);
        }

        return byName;
    }
}
