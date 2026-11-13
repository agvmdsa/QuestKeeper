using System.Globalization;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaScriptOptionsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skualink-options-test", "options"));

    public enum Mode
    {
        Fast,
        Very_Slow,
    }

    private static SkuaOptionDescriptor Option(string name, Type? type, object? defaultValue = null, string category = "Options", string? displayName = null, string? description = null) =>
        new(category, name, displayName ?? name.ToUpperInvariant(), description ?? $"about {name}", type, defaultValue);

    // ---- type mapping -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(bool), "bool")]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(Mode), "enum")]
    [InlineData(typeof(long), "other")]
    [InlineData(typeof(float), "other")]
    [InlineData(typeof(short), "other")]
    [InlineData(typeof(decimal), "other")]
    [InlineData(typeof(DateTime), "other")]
    [InlineData(typeof(char), "other")]
    [InlineData(null, "other")]
    public void Types_map_to_the_protocol_names(Type? type, string expected)
    {
        Assert.Equal(expected, SkuaScriptOptions.TypeOf(type));
    }

    // ---- definitions ------------------------------------------------------------------------------------------------

    [Fact]
    public void Definitions_carry_name_display_name_description_type_and_default()
    {
        var defs = SkuaScriptOptions.Definitions(
        [
            Option("Follow", typeof(string), "Someone", displayName: "Player to follow", description: "Who to follow"),
            Option("Loop", typeof(bool), true),
            Option("Count", typeof(int), 25),
        ]);

        Assert.Equal(
            [
                new SkuaOptionDef("Options", "Follow", "Player to follow", "Who to follow", "string", "Someone"),
                new SkuaOptionDef("Options", "Loop", "LOOP", "about Loop", "bool", "True"),
                new SkuaOptionDef("Options", "Count", "COUNT", "about Count", "int", "25"),
            ],
            defs);
    }

    [Fact]
    public void Enum_options_list_their_members_the_way_skua_shows_and_saves_them()
    {
        var defs = SkuaScriptOptions.Definitions([Option("Speed", typeof(Mode), Mode.Very_Slow)]);

        var def = Assert.Single(defs);
        Assert.Equal("enum", def.Type);
        Assert.Equal("Very Slow", def.Default);
        Assert.Equal(["Fast", "Very Slow"], def.Choices);
    }

    [Fact]
    public void Numbers_are_written_in_invariant_culture_whatever_the_pc_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var defs = SkuaScriptOptions.Definitions([Option("Ratio", typeof(double), 1.5), Option("Big", typeof(int), 12000)]);

            Assert.Equal(["1.5", "12000"], defs.Select(d => d.Default));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_missing_default_display_name_or_description_is_filled_in()
    {
        var defs = SkuaScriptOptions.Definitions([new SkuaOptionDescriptor("Options", "Name", "", null, typeof(string), null)]);

        Assert.Equal(new SkuaOptionDef("Options", "Name", "Name", "", "string", ""), Assert.Single(defs));
    }

    [Fact]
    public void Categories_are_kept_and_a_missing_category_is_the_default_one()
    {
        var defs = SkuaScriptOptions.Definitions(
        [
            Option("A", typeof(bool), false, category: "Class_Options"),
            new SkuaOptionDescriptor(null!, "B", "B", "", typeof(bool), false),
        ]);

        Assert.Equal(["Class_Options", "Options"], defs.Select(d => d.Category));
    }

    [Fact]
    public void Nameless_and_repeated_options_are_left_out()
    {
        var defs = SkuaScriptOptions.Definitions(
        [
            Option("A", typeof(int), 1),
            Option("", typeof(int), 2),
            new SkuaOptionDescriptor("Options", null, "x", "", typeof(int), 3),
            Option("A", typeof(bool), true),
            Option("A", typeof(bool), true, category: "Other"),
        ]);

        Assert.Equal([("Options", "A", "int"), ("Other", "A", "bool")], defs.Select(d => (d.Category, d.Name, d.Type)));
    }

    [Fact]
    public void A_default_whose_text_cannot_be_read_is_empty()
    {
        var defs = SkuaScriptOptions.Definitions([Option("X", typeof(string), new Throwing())]);

        Assert.Equal("", Assert.Single(defs).Default);
    }

    // ---- saved values -----------------------------------------------------------------------------------------------

    [Fact]
    public void Saved_values_keep_only_known_options_in_definition_order_with_enum_members_as_skua_shows_them()
    {
        var defs = SkuaScriptOptions.Definitions(
        [
            Option("Speed", typeof(Mode), Mode.Fast),
            Option("Loop", typeof(bool), false),
            Option("Kill", typeof(bool), false, category: "Extra"),
        ]);
        var file = SkuaOptionsFile.Parse(["Options:Loop=True", "Options:Gone=1", "Extra:Kill=False", "Options:Speed=Very_Slow", "Nope:Kill=True"]);

        var values = SkuaScriptOptions.SavedValues(defs, file);

        Assert.Equal(
            [new SkuaOptionValue("Options", "Speed", "Very Slow"), new SkuaOptionValue("Options", "Loop", "True"), new SkuaOptionValue("Extra", "Kill", "False")],
            values);
    }

    [Fact]
    public void Merged_values_take_the_changes_over_the_saved_ones_in_definition_order()
    {
        var defs = SkuaScriptOptions.Definitions([Option("A", typeof(int), 1), Option("B", typeof(int), 2), Option("C", typeof(int), 3)]);
        IReadOnlyList<SkuaOptionValue> saved = [new("Options", "C", "30"), new("Options", "A", "10")];
        IReadOnlyList<SkuaOptionValue> changes = [new("Options", "B", "20"), new("Options", "A", "11"), new("Options", "A", "12")];

        var merged = SkuaScriptOptions.Merge(defs, saved, changes);

        Assert.Equal([new SkuaOptionValue("Options", "A", "12"), new SkuaOptionValue("Options", "B", "20"), new SkuaOptionValue("Options", "C", "30")], merged);
    }

    [Fact]
    public void Doubles_are_read_from_and_written_to_the_file_in_the_pc_culture()
    {
        var german = new CultureInfo("de-DE");
        var defs = SkuaScriptOptions.Definitions([Option("Ratio", typeof(double), 0.5), Option("Name", typeof(string), "")]);

        var saved = SkuaScriptOptions.SavedValues(defs, [new("Options", "Ratio", "1,5"), new("Options", "Name", "1,5")], german);
        var file = SkuaScriptOptions.ForFile(defs, [new("Options", "Ratio", "2.25"), new("Options", "Name", "2.25")], german);

        Assert.Equal([new SkuaOptionValue("Options", "Ratio", "1.5"), new SkuaOptionValue("Options", "Name", "1,5")], saved);
        Assert.Equal([new SkuaOptionValue("Options", "Ratio", "2,25"), new SkuaOptionValue("Options", "Name", "2.25")], file);
    }

    [Fact]
    public void A_saved_double_that_does_not_read_in_the_pc_culture_is_kept_as_it_is()
    {
        var defs = SkuaScriptOptions.Definitions([Option("Ratio", typeof(double), 0.5)]);

        var saved = SkuaScriptOptions.SavedValues(defs, [new("Options", "Ratio", "lots")], new CultureInfo("de-DE"));

        Assert.Equal("lots", Assert.Single(saved).Value);
    }

    // ---- storage name from the script source ---------------------------------------------------------------------

    [Theory]
    [InlineData("public class Butler\n{\n    public string OptionsStorage = \"Butler\";\n}", "Butler")]
    [InlineData("    public readonly string OptionsStorage=\"Farm Gold\" ;", "Farm Gold")]
    [InlineData("public string OptionsStorage = \"A\";\npublic string OptionsStorage = \"A\";", "A")]
    public void The_storage_name_comes_from_the_script_public_field(string source, string expected)
    {
        Assert.Equal(expected, SkuaScriptOptions.StorageName(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("public class X { }")]
    [InlineData("// public string OptionsStorage = \"Old\";")]
    [InlineData("private string OptionsStorage = \"Hidden\";")]
    [InlineData("public string OptionsStorage = Name + \"x\";")]
    [InlineData("public string OptionsStorage = \"A\";\npublic string OptionsStorage = \"B\";")]
    [InlineData("public string OptionsStorage = \"../evil\";")]
    [InlineData("public string OptionsStorage = \"a/b\";")]
    [InlineData("public string OptionsStorage = \"a\\\\b\";")]
    [InlineData("public string OptionsStorage = \"C:x\";")]
    [InlineData("public string OptionsStorage = \"..\";")]
    [InlineData("public string OptionsStorage = \" padded \";")]
    [InlineData("public string OptionsStorage = \"\";")]
    public void A_storage_name_that_is_not_a_plain_literal_or_not_a_safe_file_name_is_not_used(string source)
    {
        Assert.Null(SkuaScriptOptions.StorageName(source));
    }

    [Fact]
    public void No_source_has_no_storage_name()
    {
        Assert.Null(SkuaScriptOptions.StorageName(null));
    }

    // ---- options file path ------------------------------------------------------------------------------------------

    [Fact]
    public void Only_a_cfg_file_inside_the_options_folder_may_be_written()
    {
        Assert.True(SkuaScriptOptions.IsOptionsFile(Root, Path.Combine(Root, "Butler.cfg")));
        Assert.True(SkuaScriptOptions.IsOptionsFile(Root, Path.Combine(Root, "Sub", "Farm.CFG")));
        Assert.True(SkuaScriptOptions.IsOptionsFile(Root + Path.DirectorySeparatorChar, Path.Combine(Root, "a.cfg")));

        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, Path.Combine(Root, "..", "evil.cfg")));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, Path.Combine(Root + "2", "a.cfg")));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, Path.Combine(Root, "a.txt")));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, Path.Combine(Root, ".cfg", "x")));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, Root));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, ""));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, null));
        Assert.False(SkuaScriptOptions.IsOptionsFile("", Path.Combine(Root, "a.cfg")));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, "a.cfg"));
        Assert.False(SkuaScriptOptions.IsOptionsFile(Root, "bad\0.cfg"));
    }

    // ---- options dialog match ---------------------------------------------------------------------------------------

    [Fact]
    public void The_dialog_is_matched_by_its_view_model_type_name_and_the_script_container()
    {
        var container = new object();

        Assert.True(SkuaScriptOptions.IsOptionsDialog(new OptionContainerViewModel(container), container));
        Assert.False(SkuaScriptOptions.IsOptionsDialog(new OptionContainerViewModel(new object()), container));
        Assert.False(SkuaScriptOptions.IsOptionsDialog(new OptionContainerViewModel(null), container));
        Assert.False(SkuaScriptOptions.IsOptionsDialog(new OtherViewModel(container), container));
        Assert.False(SkuaScriptOptions.IsOptionsDialog(null, container));
        Assert.False(SkuaScriptOptions.IsOptionsDialog(new ThrowingContainer.OptionContainerViewModel(), container));
    }

    [Fact]
    public void Without_a_container_to_compare_the_type_name_alone_decides()
    {
        Assert.True(SkuaScriptOptions.IsOptionsDialog(new OptionContainerViewModel(new object()), null));
        Assert.True(SkuaScriptOptions.IsOptionsDialog(new NoContainer.OptionContainerViewModel(), new object()));
        Assert.False(SkuaScriptOptions.IsOptionsDialog(new OtherViewModel(null), null));
    }

    private sealed class Throwing
    {
        public override string ToString() => throw new InvalidOperationException("no text");
    }

    private sealed class OptionContainerViewModel(object? container)
    {
        public object? Container { get; } = container;
    }

    private sealed class OtherViewModel(object? container)
    {
        public object? Container { get; } = container;
    }

    private static class ThrowingContainer
    {
        public sealed class OptionContainerViewModel
        {
            public object Container => throw new InvalidOperationException("gone");
        }
    }

    private static class NoContainer
    {
        public sealed class OptionContainerViewModel;
    }
}
