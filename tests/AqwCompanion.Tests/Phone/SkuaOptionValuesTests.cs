using System.Globalization;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Phone;

public class SkuaOptionValuesTests
{
    private static readonly CultureInfo Comma = CultureInfo.GetCultureInfo("de-DE");

    private static readonly IReadOnlyList<SkuaOptionDef> Defs =
    [
        new("Options", "Rate", "Rate", "", "double", "1.5"),
        new("Options", "Mode", "Mode", "", "enum", "Fast Farm", ["Fast Farm", "Slow"]),
        new("Options", "Name", "Name", "", "string", ""),
        new("Options", "On", "On", "", "bool", "False"),
    ];

    [Fact]
    public void Raw_file_values_become_invariant_numbers_and_enum_members_as_shown()
    {
        var values = SkuaOptionValues.FromPlugin(
            Defs, [new("Options", "Rate", "1,5"), new("Options", "Mode", "Fast_Farm"), new("Options", "Name", "a_b"), new("Options", "Other", "1,5")], raw: true, Comma);

        Assert.Equal(["1.5", "Fast Farm", "a_b", "1,5"], values.Select(v => v.Value));
    }

    [Fact]
    public void Values_from_a_plugin_with_definitions_or_without_known_definitions_are_left_alone()
    {
        IReadOnlyList<SkuaOptionValue> values = [new("Options", "Rate", "1,5")];

        Assert.Same(values, SkuaOptionValues.FromPlugin(Defs, values, raw: false, Comma));
        Assert.Same(values, SkuaOptionValues.FromPlugin(null, values, raw: true, Comma));
    }

    [Fact]
    public void An_unreadable_raw_number_is_kept_as_it_is()
    {
        Assert.Equal("fast", SkuaOptionValues.FromPlugin(Defs, [new("Options", "Rate", "fast")], raw: true, Comma)[0].Value);
    }

    [Fact]
    public void Tidy_fixes_boolean_case_and_enum_underscores_only()
    {
        var values = SkuaOptionValues.Tidy(
            Defs, [new("Options", "On", "true"), new("Options", "Mode", "Fast_Farm"), new("Options", "Mode", "Medium_Farm"), new("Options", "Name", "TRUE")]);

        Assert.Equal(["True", "Fast Farm", "Medium_Farm", "TRUE"], values.Select(v => v.Value));
    }

    [Fact]
    public void For_a_plugin_without_definitions_doubles_go_in_the_pc_culture()
    {
        IReadOnlyList<SkuaOptionValue> values = [new("Options", "Rate", "1.5"), new("Options", "Mode", "Fast Farm")];

        Assert.Equal(["1,5", "Fast Farm"], SkuaOptionValues.ForPlugin(Defs, values, raw: true, Comma).Select(v => v.Value));
        Assert.Same(values, SkuaOptionValues.ForPlugin(Defs, values, raw: false, Comma));
        Assert.Equal(["1.5", "Fast Farm"], SkuaOptionValues.ForPlugin(Defs, values, raw: true, CultureInfo.InvariantCulture).Select(v => v.Value));
    }
}
