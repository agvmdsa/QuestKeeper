using System.Globalization;

namespace AqwCompanion.Skua;

/// <summary>
/// Skua's per-script options file (<c>OptionContainer.Save/Load</c> in Skua 1.4.5.0): one option per line,
/// <c>Options:Name=Value</c> for the default category and <c>Category:Name=Value</c> otherwise; older files also hold bare
/// <c>Name:Value</c> or <c>Name=Value</c>. Skua trims each line and splits it on the first two of <c>:</c> and <c>=</c>, so a
/// value keeps any later separator. Pure: lines in, lines out.
/// </summary>
public static class SkuaOptionsFile
{
    /// <summary>The category Skua writes as <c>Options</c> (the script's plain <c>Options</c> list).</summary>
    public const string DefaultCategory = "Options";

    private static readonly char[] Separators = [':', '='];

    /// <summary>The values in the file, the last line winning for a repeated name; blank and unreadable lines are skipped.</summary>
    public static IReadOnlyList<SkuaOptionValue> Parse(IEnumerable<string> lines)
    {
        var values = new List<SkuaOptionValue>();
        var index = new Dictionary<(string, string), int>();
        foreach (var raw in lines)
        {
            if (raw is null)
            {
                continue;
            }

            var parts = raw.TrimStart('\uFEFF').Trim().Split(Separators, 3);
            var (category, name, value) = parts.Length switch
            {
                3 => (parts[0], parts[1], parts[2]),
                2 => (DefaultCategory, parts[0], parts[1]),
                _ => ("", "", ""),
            };
            if (category.Length == 0 || name.Length == 0)
            {
                continue;
            }

            var option = new SkuaOptionValue(category, name, value);
            if (index.TryGetValue((category, name), out var at))
            {
                values[at] = option;
            }
            else
            {
                index[(category, name)] = values.Count;
                values.Add(option);
            }
        }

        return values;
    }

    /// <summary>The lines Skua writes for these values. A value that could not be read back the same is left out.</summary>
    public static IReadOnlyList<string> Format(IReadOnlyList<SkuaOptionValue> values) =>
        values.Where(RoundTrips).Select(v => $"{v.Category}:{v.Name}={v.Value}").ToList();

    /// <summary>
    /// Null when every value may be written for these definitions, otherwise the first problem as text: an unknown name
    /// (in its category), a value over <see cref="SkuaProtocol.MaxOptionValueLength"/> characters, a line break, trailing
    /// white space (Skua trims it away), or a value that does not fit the declared type (bool <c>True</c>/<c>False</c>,
    /// int, double in invariant culture, a member of the enum's choices).
    /// </summary>
    public static string? Validate(IReadOnlyList<SkuaOptionDef> defs, IReadOnlyList<SkuaOptionValue> values)
    {
        var known = new Dictionary<(string, string), SkuaOptionDef>();
        foreach (var def in defs)
        {
            known.TryAdd((def.Category, def.Name), def);
        }

        foreach (var value in values)
        {
            var label = value.Category == DefaultCategory ? value.Name : $"{value.Category}:{value.Name}";
            if (!known.TryGetValue((value.Category, value.Name), out var def))
            {
                return $"unknown option {label}";
            }

            var text = value.Value;
            if (text.Length > SkuaProtocol.MaxOptionValueLength)
            {
                return $"value too long for {label}";
            }

            if (text.Any(IsLineBreak))
            {
                return $"line break in {label}";
            }

            if (text.Length > 0 && char.IsWhiteSpace(text[^1]))
            {
                return $"space at the end of {label}";
            }

            if (!FitsType(def, text))
            {
                return $"not a valid {def.Type} for {label}";
            }
        }

        return null;
    }

    private static bool FitsType(SkuaOptionDef def, string text) => def.Type switch
    {
        "bool" => text is "True" or "False",
        "int" => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
        "double" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
        "enum" => def.Choices is { } choices && choices.Contains(text, StringComparer.Ordinal),
        _ => true,
    };

    private static bool IsLineBreak(char c) => c is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029';

    private static bool RoundTrips(SkuaOptionValue v) =>
        v.Category.Length > 0 && v.Name.Length > 0
        && !char.IsWhiteSpace(v.Category[0]) && v.Category[0] != '\uFEFF'
        && v.Category.IndexOfAny(Separators) < 0 && v.Name.IndexOfAny(Separators) < 0
        && !v.Category.Any(IsLineBreak) && !v.Name.Any(IsLineBreak) && !v.Value.Any(IsLineBreak)
        && (v.Value.Length == 0 || !char.IsWhiteSpace(v.Value[^1]));
}
