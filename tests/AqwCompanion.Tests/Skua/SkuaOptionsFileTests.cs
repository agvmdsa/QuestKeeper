using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaOptionsFileTests
{
    private static SkuaOptionValue V(string category, string name, string value) => new(category, name, value);

    private static SkuaOptionDef Def(string name, string type, string category = "Options", IReadOnlyList<string>? choices = null) =>
        new(category, name, name, "", type, "", choices);

    [Fact]
    public void Lines_in_skua_format_are_read_with_their_category()
    {
        var values = SkuaOptionsFile.Parse(["Options:PlayerName=Artix", "Core:Rest=True", "Bank Settings:Keep=1"]);

        Assert.Equal([V("Options", "PlayerName", "Artix"), V("Core", "Rest", "True"), V("Bank Settings", "Keep", "1")], values);
    }

    [Fact]
    public void Bare_lines_belong_to_the_default_category()
    {
        var values = SkuaOptionsFile.Parse(["PlayerName:Artix", "Rest=True"]);

        Assert.Equal([V("Options", "PlayerName", "Artix"), V("Options", "Rest", "True")], values);
    }

    [Fact]
    public void A_value_keeps_every_later_separator()
    {
        var values = SkuaOptionsFile.Parse(["Options:Url=https://x.y/?a=b", "Core:Pair=k:v=w"]);

        Assert.Equal([V("Options", "Url", "https://x.y/?a=b"), V("Core", "Pair", "k:v=w")], values);
    }

    [Fact]
    public void Either_separator_works_in_either_place_like_skua()
    {
        var values = SkuaOptionsFile.Parse(["Options=Name:Value", "Core=Rest=True"]);

        Assert.Equal([V("Options", "Name", "Value"), V("Core", "Rest", "True")], values);
    }

    [Fact]
    public void An_empty_value_is_kept()
    {
        Assert.Equal([V("Options", "PlayerName", "")], SkuaOptionsFile.Parse(["Options:PlayerName="]));
    }

    [Fact]
    public void Lines_are_trimmed_like_skua_trims_them()
    {
        var values = SkuaOptionsFile.Parse(["  Options:PlayerName=Artix  ", "\tCore:Rest=True\t"]);

        Assert.Equal([V("Options", "PlayerName", "Artix"), V("Core", "Rest", "True")], values);
    }

    [Fact]
    public void A_byte_order_mark_is_ignored()
    {
        var values = SkuaOptionsFile.Parse(["\uFEFFOptions:PlayerName=Artix", "Core:Rest=True"]);

        Assert.Equal([V("Options", "PlayerName", "Artix"), V("Core", "Rest", "True")], values);
    }

    [Fact]
    public void Blank_and_garbage_lines_are_skipped()
    {
        var values = SkuaOptionsFile.Parse(
            ["", "   ", "just text", "::", ":=x", "Options:=x", ":Name=x", "=x", "Options:Kept=1", null!]);

        Assert.Equal([V("Options", "Kept", "1")], values);
    }

    [Fact]
    public void The_last_line_for_a_name_wins_like_skua()
    {
        var values = SkuaOptionsFile.Parse(["Options:Rest=False", "Core:Rest=1", "Rest=True"]);

        Assert.Equal([V("Options", "Rest", "True"), V("Core", "Rest", "1")], values);
    }

    [Fact]
    public void Values_are_written_in_skua_format()
    {
        var lines = SkuaOptionsFile.Format([V("Options", "PlayerName", "Artix"), V("Core", "Rest", "True"), V("Options", "Url", "a=b:c")]);

        Assert.Equal(["Options:PlayerName=Artix", "Core:Rest=True", "Options:Url=a=b:c"], lines);
    }

    [Fact]
    public void Writing_and_reading_gives_the_same_values()
    {
        IReadOnlyList<SkuaOptionValue> values =
            [V("Options", "PlayerName", "Artix"), V("Core", "Rest", "True"), V("Options", "Url", "https://x.y/?a=b"), V("Bank", "Empty", "")];

        Assert.Equal(values, SkuaOptionsFile.Parse(SkuaOptionsFile.Format(values)));
    }

    [Fact]
    public void A_value_that_could_not_be_read_back_is_not_written()
    {
        var lines = SkuaOptionsFile.Format(
        [
            V("Options", "Line", "a\nb"), V("Options", "Return", "a\rb"), V("Options", "Na:me", "x"), V("Options", "Na=me", "x"),
            V("Ca:t", "Name", "x"), V("", "Name", "x"), V("Options", "", "x"), V(" Cat", "Name", "x"), V("Options", "Tail", "x "),
            V("Options", "Kept", "1"),
        ]);

        Assert.Equal(["Options:Kept=1"], lines);
    }

    [Fact]
    public void Valid_values_of_every_type_pass()
    {
        IReadOnlyList<SkuaOptionDef> defs =
        [
            Def("B", "bool"), Def("I", "int"), Def("D", "double"), Def("S", "string"), Def("E", "enum", choices: ["Near", "Far"]),
            Def("O", "other"), Def("C", "bool", category: "Core"),
        ];

        var error = SkuaOptionsFile.Validate(defs,
        [
            V("Options", "B", "True"), V("Options", "B", "False"), V("Options", "I", "-42"), V("Options", "D", "1.5"), V("Options", "D", "-3"),
            V("Options", "S", "any text: a=b"), V("Options", "S", ""), V("Options", "E", "Far"), V("Options", "O", "whatever"),
            V("Core", "C", "False"),
        ]);

        Assert.Null(error);
    }

    [Fact]
    public void An_empty_list_is_valid()
    {
        Assert.Null(SkuaOptionsFile.Validate([Def("B", "bool")], []));
    }

    [Theory]
    [InlineData("bool", "true")]
    [InlineData("bool", "1")]
    [InlineData("bool", "")]
    [InlineData("int", "1.5")]
    [InlineData("int", "abc")]
    [InlineData("int", "99999999999")]
    [InlineData("int", "")]
    [InlineData("double", "1,5")]
    [InlineData("double", "NaN")]
    [InlineData("double", "Infinity")]
    [InlineData("double", "abc")]
    [InlineData("double", "")]
    [InlineData("enum", "near")]
    [InlineData("enum", "Other")]
    public void A_value_that_does_not_fit_its_type_is_refused(string type, string value)
    {
        var defs = new[] { Def("X", type, choices: type == "enum" ? ["Near", "Far"] : null) };

        var error = SkuaOptionsFile.Validate(defs, [V("Options", "X", value)]);

        Assert.NotNull(error);
        Assert.Contains("X", error);
    }

    [Fact]
    public void An_enum_without_reported_choices_is_refused()
    {
        Assert.NotNull(SkuaOptionsFile.Validate([Def("E", "enum")], [V("Options", "E", "Near")]));
    }

    [Fact]
    public void An_unknown_name_is_refused()
    {
        var error = SkuaOptionsFile.Validate([Def("Known", "string")], [V("Options", "Known", "a"), V("Options", "Unknown", "b")]);

        Assert.NotNull(error);
        Assert.Contains("Unknown", error);
    }

    [Fact]
    public void A_known_name_in_another_category_is_refused()
    {
        Assert.NotNull(SkuaOptionsFile.Validate([Def("Rest", "bool", category: "Core")], [V("Options", "Rest", "True")]));
    }

    [Fact]
    public void A_value_over_200_characters_is_refused()
    {
        var defs = new[] { Def("S", "string") };

        Assert.Null(SkuaOptionsFile.Validate(defs, [V("Options", "S", new string('a', SkuaProtocol.MaxOptionValueLength))]));
        Assert.NotNull(SkuaOptionsFile.Validate(defs, [V("Options", "S", new string('a', SkuaProtocol.MaxOptionValueLength + 1))]));
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\u2028b")]
    [InlineData("a\u0085b")]
    public void A_value_with_a_line_break_is_refused(string value)
    {
        Assert.NotNull(SkuaOptionsFile.Validate([Def("S", "string")], [V("Options", "S", value)]));
    }

    [Fact]
    public void A_value_with_spaces_at_its_ends_is_refused_because_skua_would_trim_them()
    {
        var defs = new[] { Def("S", "string") };

        Assert.NotNull(SkuaOptionsFile.Validate(defs, [V("Options", "S", "Artix ")]));
        Assert.Null(SkuaOptionsFile.Validate(defs, [V("Options", "S", "Ar tix")]));
    }

    [Fact]
    public void Every_valid_value_survives_writing_and_reading()
    {
        IReadOnlyList<SkuaOptionDef> defs = [Def("S", "string"), Def("D", "double", category: "Core")];
        IReadOnlyList<SkuaOptionValue> values = [V("Options", "S", "a=b:c"), V("Core", "D", "2.25")];

        Assert.Null(SkuaOptionsFile.Validate(defs, values));
        Assert.Equal(values, SkuaOptionsFile.Parse(SkuaOptionsFile.Format(values)));
    }
}
