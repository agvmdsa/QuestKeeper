using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AqwCompanion.Skua;

/// <summary>
/// One option a script declares, as Skua holds it (<c>IOption</c> in Skua 1.4.5.0): the plugin copies these from
/// <c>Config.Options</c> (category <see cref="SkuaOptionsFile.DefaultCategory"/>) and <c>Config.MultipleOptions</c>
/// (category = the dictionary key, which is what Skua writes in the options file).
/// </summary>
public sealed record SkuaOptionDescriptor(string Category, string? Name, string? DisplayName, string? Description, Type? Type, object? DefaultValue);

/// <summary>
/// Skua's options of the loaded script after a start: <paramref name="ScriptPath"/> is Skua's <c>LoadedScript</c>,
/// <paramref name="OptionsFile"/> is <c>Config.OptionsFile</c> (<c>%AppData%\Skua\options\{Storage}.cfg</c>).
/// </summary>
public sealed record SkuaLoadedOptions(string? ScriptPath, string? OptionsFile, IReadOnlyList<SkuaOptionDescriptor> Options);

/// <summary>What the plugin sees of the script's options dialog. <see cref="Unknown"/>: the windows could not be looked at.</summary>
public enum SkuaOptionsWindow
{
    None,
    Open,
    Unknown,
}

/// <summary>
/// What the command handler needs from Skua for script options. The plugin implements it over Skua's <c>Config</c>, the
/// options folder and Skua's WPF windows; the tests fake it. Any member may throw: the handler turns every failure into
/// an error answer or the "waiting for you on the PC" state.
/// </summary>
public interface ISkuaOptionsHost
{
    /// <summary>Skua's options folder (<c>ClientFileSources.SkuaOptionsDIR</c>); nothing outside it is read or written.</summary>
    string OptionsRoot { get; }

    /// <summary>The culture Skua reads and writes option numbers with (<c>Convert.ChangeType</c>, <c>ToString</c>): the PC's.</summary>
    CultureInfo FileCulture { get; }

    /// <summary>The options of the script Skua loaded at its last start; null when Skua has none.</summary>
    SkuaLoadedOptions? ReadLoadedOptions();

    /// <summary>The text of a script (to find its <c>OptionsStorage</c>); null when it cannot be read.</summary>
    string? ReadScriptSource(string path);

    /// <summary>The lines of an options file; null when it does not exist.</summary>
    IReadOnlyList<string>? ReadOptionsFile(string path);

    /// <summary>Replaces an options file with these lines.</summary>
    void WriteOptionsFile(string path, IReadOnlyList<string> lines);

    /// <summary>Whether the options dialog of the script Skua started last is open.</summary>
    SkuaOptionsWindow FindOptionsWindow();

    /// <summary>Closes that dialog (Skua then saves the values it shows, which it loaded from the options file).</summary>
    void CloseOptionsWindow();
}

/// <summary>Pure helpers for script options: definitions from Skua's options, saved values, merges, and the safety checks.</summary>
public static partial class SkuaScriptOptions
{
    /// <summary>The type name of the view model Skua's options dialog (<c>HostDialog</c>) shows.</summary>
    public const string DialogViewModelName = "OptionContainerViewModel";

    /// <summary>The protocol type for a .NET option type: <c>bool|int|double|string|enum</c>, anything else <c>other</c>.</summary>
    public static string TypeOf(Type? type)
    {
        if (type is null)
        {
            return "other";
        }

        if (type.IsEnum)
        {
            return "enum";
        }

        return type == typeof(bool) ? "bool"
            : type == typeof(int) ? "int"
            : type == typeof(double) ? "double"
            : type == typeof(string) ? "string"
            : "other";
    }

    /// <summary>
    /// The definitions SkuaLink shows, in Skua's order. Nameless options and a repeated name in one category are left out.
    /// Enum members and defaults are written with spaces for underscores, the way Skua's dialog shows and saves them;
    /// numbers in invariant culture. A display name that is empty is the name.
    /// </summary>
    public static IReadOnlyList<SkuaOptionDef> Definitions(IEnumerable<SkuaOptionDescriptor> options)
    {
        var defs = new List<SkuaOptionDef>();
        var seen = new HashSet<(string, string)>();
        foreach (var option in options)
        {
            if (option is null || string.IsNullOrEmpty(option.Name))
            {
                continue;
            }

            var category = string.IsNullOrEmpty(option.Category) ? SkuaOptionsFile.DefaultCategory : option.Category;
            if (!seen.Add((category, option.Name)))
            {
                continue;
            }

            var type = TypeOf(option.Type);
            IReadOnlyList<string>? choices = type == "enum" ? Enum.GetNames(option.Type!).Select(EnumText).ToList() : null;
            defs.Add(new SkuaOptionDef(
                category,
                option.Name,
                string.IsNullOrEmpty(option.DisplayName) ? option.Name : option.DisplayName,
                option.Description ?? "",
                type,
                ValueText(option.DefaultValue),
                choices));
        }

        return defs;
    }

    /// <summary>
    /// The saved values of known options in definition order, as SkuaLink sees them: enum values as Skua's dialog shows
    /// them, and doubles in invariant culture (Skua writes them in <paramref name="fileCulture"/>, the PC's; a value that
    /// does not read in it is kept as it is).
    /// </summary>
    public static IReadOnlyList<SkuaOptionValue> SavedValues(
        IReadOnlyList<SkuaOptionDef> defs, IReadOnlyList<SkuaOptionValue> fileValues, CultureInfo? fileCulture = null)
    {
        var culture = fileCulture ?? CultureInfo.InvariantCulture;
        var byName = new Dictionary<(string, string), string>();
        foreach (var value in fileValues)
        {
            byName[(value.Category, value.Name)] = value.Value;
        }

        var values = new List<SkuaOptionValue>();
        foreach (var def in defs)
        {
            if (byName.TryGetValue((def.Category, def.Name), out var text))
            {
                values.Add(new SkuaOptionValue(def.Category, def.Name, def.Type switch
                {
                    "enum" => EnumText(text),
                    "double" => Number(text, culture, CultureInfo.InvariantCulture),
                    _ => text,
                }));
            }
        }

        return values;
    }

    /// <summary>
    /// The names in an options file as untyped definitions (type <c>string</c>, so <see cref="SkuaOptionsFile.Validate"/>
    /// checks the name and only the type-free rules), in file order.
    /// </summary>
    public static IReadOnlyList<SkuaOptionDef> Untyped(IReadOnlyList<SkuaOptionValue> fileValues) =>
        fileValues.Select(v => new SkuaOptionDef(v.Category, v.Name, v.Name, "", "string", "")).ToList();

    /// <summary>The saved values with the changes applied (a later change of one option wins), in definition order.</summary>
    public static IReadOnlyList<SkuaOptionValue> Merge(
        IReadOnlyList<SkuaOptionDef> defs, IReadOnlyList<SkuaOptionValue> saved, IReadOnlyList<SkuaOptionValue> changes)
    {
        var byName = new Dictionary<(string, string), string>();
        foreach (var value in saved.Concat(changes))
        {
            byName[(value.Category, value.Name)] = value.Value;
        }

        return defs
            .Where(d => byName.ContainsKey((d.Category, d.Name)))
            .Select(d => new SkuaOptionValue(d.Category, d.Name, byName[(d.Category, d.Name)]))
            .ToList();
    }

    /// <summary>
    /// The values as Skua reads them from the file: doubles (checked invariant by <see cref="SkuaOptionsFile.Validate"/>)
    /// in <paramref name="fileCulture"/>, because Skua converts them with the PC's culture (in a comma culture <c>1.5</c>
    /// would read as 15). Everything else unchanged.
    /// </summary>
    public static IReadOnlyList<SkuaOptionValue> ForFile(
        IReadOnlyList<SkuaOptionDef> defs, IReadOnlyList<SkuaOptionValue> values, CultureInfo? fileCulture = null)
    {
        var culture = fileCulture ?? CultureInfo.InvariantCulture;
        var doubles = defs.Where(d => d.Type == "double").Select(d => (d.Category, d.Name)).ToHashSet();
        return values
            .Select(v => doubles.Contains((v.Category, v.Name)) ? v with { Value = Number(v.Value, CultureInfo.InvariantCulture, culture) } : v)
            .ToList();
    }

    /// <summary>
    /// The storage name a script declares, which names its options file (<c>{storage}.cfg</c> in Skua's options folder):
    /// the value of a plain <c>public string OptionsStorage = "...";</c> line (Skua reads that public field). Null when
    /// there is none, when lines disagree, when it is not a plain literal on one uncommented line, or when it is not a safe
    /// file name (no path, no <c>..</c>, no characters Windows refuses, no space at either end). Pure: text in, name out.
    /// </summary>
    public static string? StorageName(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        try
        {
            var names = StorageField().Matches(source).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToList();
            return names.Count == 1 && IsSafeFileName(names[0]) ? names[0] : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>The options file Skua uses for a storage name (<c>ScriptOptionContainer.OptionsFile</c>).</summary>
    public static string OptionsFileFor(string root, string storage) => Path.Combine(root, storage + ".cfg");

    /// <summary>True only for an absolute path to a <c>.cfg</c> file inside <paramref name="root"/> (after resolving <c>..</c>).</summary>
    public static bool IsOptionsFile(string? root, string? path)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(root))
        {
            return false;
        }

        try
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                && full.Length > fullRoot.Length
                && Path.GetExtension(full).Equals(".cfg", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(full).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// True when a window's <c>DataContext</c> is Skua's options view model (matched by type name through reflection, no
    /// Skua.Core reference) for <paramref name="container"/>, the script's <c>Config</c>: a view model whose
    /// <c>Container</c> is another container (a plugin's options) does not match. With no container to compare, or a view
    /// model without that property, the type name alone decides. Never throws.
    /// </summary>
    public static bool IsOptionsDialog(object? dataContext, object? container)
    {
        try
        {
            if (dataContext is null || dataContext.GetType().Name != DialogViewModelName)
            {
                return false;
            }

            if (container is null || dataContext.GetType().GetProperty("Container", BindingFlags.Public | BindingFlags.Instance) is not { } property)
            {
                return true;
            }

            return ReferenceEquals(property.GetValue(dataContext), container);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string EnumText(string name) => name.Replace('_', ' ');

    [GeneratedRegex(@"^[ \t]*public[ \t]+(?:readonly[ \t]+)?string[ \t]+OptionsStorage[ \t]*=[ \t]*""(?<name>[^""\\\r\n]*)""[ \t]*;", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StorageField();

    private static bool IsSafeFileName(string name) =>
        name.Length is > 0 and <= 100
        && name.Trim() == name
        && name is not ("." or "..")
        && !name.EndsWith('.')
        && name.All(c => !char.IsControl(c) && c is not ('\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|'));

    private static string Number(string text, CultureInfo from, CultureInfo to) =>
        double.TryParse(text, NumberStyles.Float, from, out var number) && double.IsFinite(number) ? number.ToString(to) : text;

    private static string ValueText(object? value)
    {
        try
        {
            return value switch
            {
                null => "",
                bool b => b ? "True" : "False",
                Enum e => EnumText(e.ToString()),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };
        }
        catch (Exception)
        {
            return "";
        }
    }
}
